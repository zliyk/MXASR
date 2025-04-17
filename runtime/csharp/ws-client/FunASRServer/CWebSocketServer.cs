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
    /// WebSocket代理服务器，用于转发客户端与FunASR服务器之间的通信
    /// </summary>
    public class CWebSocketServer
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts;
        // 保存客户端WebSocket和对应的FunASR服务器连接
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
        /// 创建WebSocket代理服务器
        /// </summary>
        /// <param name="targetServerUrl">目标FunASR服务器的WebSocket地址</param>
        public CWebSocketServer(string targetServerUrl = "ws://124.223.76.169:10095/")
        {
            _listener = new HttpListener();
            _cts = new CancellationTokenSource();
            _targetServerUrl = targetServerUrl;
        }

        /// <summary>
        /// 启动WebSocket代理服务器
        /// </summary>
        /// <param name="host">监听主机名</param>
        /// <param name="port">监听端口</param>
        public async Task StartAsync(string host = "localhost", int port = 10095)
        {
            if (_isRunning)
                return;

            try
            {
                string url = $"http://{host}:{port}/ws/";
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add(url);
                _listener.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
                _listener.Start();

                _isRunning = true;

                Console.WriteLine($"WebSocket代理服务器已启动: {url}");
                Console.WriteLine($"目标FunASR服务器地址: {_targetServerUrl}");

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
                _listener.Stop();
                _isRunning = false;
                throw;
            }
        }

        /// <summary>
        /// 停止WebSocket代理服务器
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
            _listener.Stop();
            Console.WriteLine("WebSocket代理服务器已停止");
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

                    // 断开与FunASR服务器的连接
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

                // 创建到FunASR服务器的WebSocket客户端
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

                // 连接到FunASR服务器
                string status = await serverClient.ClientConnTest();
                Console.WriteLine($"客户端 {clientId} 连接到FunASR服务器: {status}");

                if (status == "WebSocket通信连接成功")
                {
                    // 设置转发FunASR服务器消息到客户端的处理器
                    serverClient.SetMessageHandler(async (serverMessage) =>
                    {
                        try
                        {
                            if (clientSocket.State == WebSocketState.Open && !sessionCts.Token.IsCancellationRequested)
                            {
                                // 转发服务器消息到客户端
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
                            Console.WriteLine($"[{clientId}] 转发服务器消息时出错: {ex.Message}");
                        }
                    });

                    // 启动客户端消息转发任务
                    await ForwardClientMessages(clientId, session);
                }
                else
                {
                    // 连接FunASR服务器失败，向客户端发送错误消息
                    var errorMessage = Encoding.UTF8.GetBytes($"{{\"status\":\"error\",\"message\":\"无法连接到FunASR服务器: {status}\"}}");
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
        /// 转发客户端消息到FunASR服务器
        /// </summary>
        /// <param name="clientId">客户端ID</param>
        /// <param name="session">客户端会话</param>
        private async Task ForwardClientMessages(string clientId, ClientSession session)
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

                    // 转发消息到FunASR服务器
                    if (receiveResult.MessageType == WebSocketMessageType.Text)
                    {
                        // 文本消息
                        string message = Encoding.UTF8.GetString(receivedData);
                        Console.WriteLine($"[{clientId}] 客户端 -> 服务器(文本): {message}");

                        // 转发文本消息
                        serverClient.SendTextMessage(message);
                    }
                    else if (receiveResult.MessageType == WebSocketMessageType.Binary)
                    {
                        // 二进制数据（音频）
                        Console.WriteLine($"[{clientId}] 客户端 -> 服务器(二进制): {receivedData.Length} 字节");

                        // 转发二进制数据
                        serverClient.SendBinaryData(receivedData);
                    }
                }
            }
            catch (Exception ex) when (ex is TaskCanceledException || ex is OperationCanceledException)
            {
                Console.WriteLine($"[{clientId}] 客户端消息转发任务已取消");
            }
            catch (Exception ex) when (ex is WebSocketException)
            {
                Console.WriteLine($"[{clientId}] WebSocket连接出错: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{clientId}] 转发客户端消息时出错: {ex.Message}");
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