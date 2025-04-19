using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FunASRServer
{
    /// <summary>
    /// WebSocket服务器，用于连接客户端与ASR服务器之间的通信
    /// </summary>
    public class CWebSocketServer
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts;
        // 保存客户端WebSocket和对应的ASR服务器连接
        private readonly ConcurrentDictionary<string, ClientSession> _clientSessions = new ConcurrentDictionary<string, ClientSession>();
        private readonly string _targetServerUrl;
        private bool _isRunning = false;

        // 表示客户端会话，包括客户端连接和对应的目标服务器连接
        private class ClientSession
        {
            public WebSocket ClientSocket { get; set; }
            public CWebSocketClient ServerClient { get; set; }
            public CancellationTokenSource CancellationSource { get; set; }
        }

        /// <summary>
        /// 创建WebSocket服务器
        /// </summary>
        /// <param name="targetServerUrl">目标ASR服务器的WebSocket地址</param>
        public CWebSocketServer(string targetServerUrl)
        {
            _listener = new HttpListener();
            _cts = new CancellationTokenSource();
            _targetServerUrl = targetServerUrl;
        }

        /// <summary>
        /// 启动WebSocket服务器
        /// </summary>
        /// <param name="host">监听主机名</param>
        /// <param name="port">监听端口</param>
        public async Task StartAsync(string host, int port)
        {
            if (_isRunning)
                return;

            try
            {
                // 注意：修改URL前缀处理方式
                string prefix;
                if (host == "0.0.0.0")
                {
                    // 对于监听所有网络接口，使用"+"而不是"0.0.0.0"
                    // 注意：需要管理员权限，或使用netsh添加URL ACL
                    prefix = $"http://+:{port}/";
                    Console.WriteLine("注意：监听所有网络接口需要管理员权限！");
                }
                else if (host == "*" || host == "+")
                {
                    prefix = $"http://+:{port}/";
                    Console.WriteLine("注意：监听所有网络接口需要管理员权限！");
                }
                else
                {
                    prefix = $"http://{host}:{port}/";
                }

                _listener.Prefixes.Clear();
                _listener.Prefixes.Add(prefix);
                _listener.AuthenticationSchemes = AuthenticationSchemes.Anonymous;

                try
                {
                    _listener.Start();
                }
                catch (HttpListenerException ex)
                {
                    // 特殊处理HttpListener的常见错误
                    if (ex.ErrorCode == 5) // 拒绝访问
                    {
                        throw new Exception("启动服务器需要管理员权限，或者使用netsh添加URL前缀访问权限", ex);
                    }
                    else
                    {
                        throw;
                    }
                }

                _isRunning = true;

                Console.WriteLine($"MXASR WebSocket服务器已启动: {prefix}");
                Console.WriteLine($"目标ASR服务器地址: {_targetServerUrl}");

                // 开始监听请求
                _ = Task.Run(async () =>
                {
                    try
                    {
                        while (_isRunning && !_cts.Token.IsCancellationRequested)
                        {
                            HttpListenerContext context = await _listener.GetContextAsync();

                            if (context.Request.IsWebSocketRequest)
                            {
                                // 处理WebSocket请求
                                ProcessWebSocketRequest(context);
                            }
                            else
                            {
                                // 返回错误响应
                                context.Response.StatusCode = 400;
                                context.Response.Close();
                            }
                        }
                    }
                    catch (Exception ex) when (ex is HttpListenerException || ex is TaskCanceledException || ex is OperationCanceledException)
                    {
                        // 服务器停止或取消时的预期异常
                        Console.WriteLine("WebSocket服务器监听循环已停止");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"WebSocket服务器监听循环出错: {ex.Message}");
                    }
                }, _cts.Token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"启动WebSocket服务器时出错: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"内部错误: {ex.InnerException.Message}");
                }

                // 避免重复关闭已经关闭的监听器
                if (_listener.IsListening)
                {
                    _listener.Stop();
                }

                _isRunning = false;
                throw;
            }
        }

        /// <summary>
        /// 停止WebSocket服务器
        /// </summary>
        public async Task StopAsync()
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            _cts.Cancel();

            // 关闭所有客户端会话
            foreach (var clientId in _clientSessions.Keys)
            {
                await CloseSession(clientId);
            }

            _clientSessions.Clear();

            // 检查监听器是否还在监听
            if (_listener.IsListening)
            {
                _listener.Stop();
            }

            Console.WriteLine("MXASR WebSocket服务器已停止");
        }

        /// <summary>
        /// 关闭客户端会话
        /// </summary>
        /// <param name="clientId">客户端ID</param>
        private async Task CloseSession(string clientId)
        {
            if (_clientSessions.TryRemove(clientId, out ClientSession session))
            {
                try
                {
                    // 取消会话相关任务
                    session.CancellationSource.Cancel();

                    // 关闭客户端WebSocket连接
                    if (session.ClientSocket.State == WebSocketState.Open)
                    {
                        await session.ClientSocket.CloseAsync(WebSocketCloseStatus.NormalClosure,
                            "服务器关闭", CancellationToken.None);
                    }

                    // 断开与ASR服务器的连接
                    await session.ServerClient.DisconnectAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"关闭客户端会话时出错: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 处理新的WebSocket连接请求
        /// </summary>
        /// <param name="context">HTTP监听器上下文</param>
        private async void ProcessWebSocketRequest(HttpListenerContext context)
        {
            WebSocketContext webSocketContext = null;
            string clientId = Guid.NewGuid().ToString();
            ClientSession session = null;

            try
            {
                // 接受客户端WebSocket连接
                webSocketContext = await context.AcceptWebSocketAsync(null);
                WebSocket clientSocket = webSocketContext.WebSocket;

                // 创建到ASR服务器的WebSocket客户端
                var serverClient = new CWebSocketClient(_targetServerUrl);

                // 创建会话取消源
                var sessionCts = new CancellationTokenSource();

                // 创建并存储会话
                session = new ClientSession
                {
                    ClientSocket = clientSocket,
                    ServerClient = serverClient,
                    CancellationSource = sessionCts
                };

                _clientSessions.TryAdd(clientId, session);

                Console.WriteLine($"客户端已连接: {clientId}");

                // 连接到MXASR服务器
                string status = await serverClient.ClientConnTest();
                Console.WriteLine($"客户端 {clientId} 连接到MXASR服务器: {status}");

                if (status == "WebSocket通信连接成功")
                {
                    // 设置处理ASR服务器消息的处理器
                    serverClient.SetMessageHandler(async (serverMessage) =>
                    {
                        try
                        {
                            if (clientSocket.State == WebSocketState.Open && !sessionCts.Token.IsCancellationRequested)
                            {
                                // 发送服务器消息到客户端
                                var messageBytes = Encoding.UTF8.GetBytes(serverMessage);
                                await clientSocket.SendAsync(
                                    new ArraySegment<byte>(messageBytes),
                                    WebSocketMessageType.Text,
                                    true,
                                    sessionCts.Token);

                                Console.WriteLine($"[{clientId}] 服务器 -> 客户端: {serverMessage}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[{clientId}] 处理服务器消息时出错: {ex.Message}");
                        }
                    });

                    // 启动客户端消息处理任务
                    await ProcessClientMessages(clientId, session);
                }
                else
                {
                    // 连接ASR服务器失败，向客户端发送错误消息
                    var errorMessage = Encoding.UTF8.GetBytes($"{{\"status\":\"error\",\"message\":\"无法连接到ASR服务器: {status}\"}}");
                    await clientSocket.SendAsync(
                        new ArraySegment<byte>(errorMessage),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None);

                    // 关闭会话
                    await CloseSession(clientId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理WebSocket请求时出错: {ex.Message}");

                // 清理会话
                if (session != null)
                {
                    await CloseSession(clientId);
                }
                else
                {
                    try
                    {
                        context.Response.StatusCode = 500;
                        context.Response.Close();
                    }
                    catch { /* 忽略关闭响应时的错误 */ }
                }
            }
        }

        /// <summary>
        /// 处理客户端消息并发送到ASR服务器
        /// </summary>
        /// <param name="clientId">客户端ID</param>
        /// <param name="session">客户端会话</param>
        private async Task ProcessClientMessages(string clientId, ClientSession session)
        {
            var buffer = new byte[1024 * 16]; // 16KB缓冲区
            var clientSocket = session.ClientSocket;
            var serverClient = session.ServerClient;
            var sessionToken = session.CancellationSource.Token;

            try
            {
                while (clientSocket.State == WebSocketState.Open && !sessionToken.IsCancellationRequested)
                {
                    // 接收客户端消息
                    WebSocketReceiveResult receiveResult = await clientSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), sessionToken);

                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        // 客户端请求关闭连接
                        await clientSocket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "客户端请求关闭",
                            CancellationToken.None);
                        break;
                    }

                    // 提取接收到的数据
                    int count = receiveResult.Count;
                    byte[] receivedData = new byte[count];
                    Array.Copy(buffer, receivedData, count);

                    // 发送消息到ASR服务器
                    if (receiveResult.MessageType == WebSocketMessageType.Text)
                    {
                        // 文本消息
                        string message = Encoding.UTF8.GetString(receivedData);
                        Console.WriteLine($"[{clientId}] 客户端 -> 服务器(文本): {message}");

                        // 发送文本消息
                        serverClient.SendTextMessage(message);
                    }
                    else if (receiveResult.MessageType == WebSocketMessageType.Binary)
                    {
                        // 二进制数据（音频）
                        Console.WriteLine($"[{clientId}] 客户端 -> 服务器(二进制): {receivedData.Length} 字节");

                        // 发送二进制数据
                        serverClient.SendBinaryData(receivedData);
                    }
                }
            }
            catch (Exception ex) when (ex is TaskCanceledException || ex is OperationCanceledException)
            {
                Console.WriteLine($"[{clientId}] 客户端消息处理任务已取消");
            }
            catch (Exception ex) when (ex is WebSocketException)
            {
                Console.WriteLine($"[{clientId}] WebSocket连接出错: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{clientId}] 处理客户端消息时出错: {ex.Message}");
            }
            finally
            {
                // 确保关闭会话
                await CloseSession(clientId);
                Console.WriteLine($"[{clientId}] 客户端会话已关闭");
            }
        }
    }
}