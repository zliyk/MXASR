using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace MXASRServer
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
        // 目标采样率，ASR服务器需要16kHz
        private const int TargetSampleRate = 16000;

        // 表示客户端会话，包括客户端连接和对应的目标服务器连接
        private class ClientSession
        {
            public WebSocket ClientSocket { get; set; }
            public CWebSocketClient ServerClient { get; set; }
            public CancellationTokenSource CancellationSource { get; set; }

            // 音频数据缓冲区
            public MemoryStream AudioBuffer { get; } = new MemoryStream();
            // 是否正在接收音频流
            public bool IsReceivingAudio { get; set; } = false;
            // 最后一次接收音频的时间
            public DateTime LastAudioReceiveTime { get; set; } = DateTime.Now;
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

                    // 释放音频缓冲区
                    session.AudioBuffer.Dispose();
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
                                // 处理ASR消息，移除特殊标记
                                string cleanedMessage = CleanAsrResponseMessage(serverMessage);

                                // 发送服务器消息到客户端
                                var messageBytes = Encoding.UTF8.GetBytes(cleanedMessage);
                                await clientSocket.SendAsync(
                                    new ArraySegment<byte>(messageBytes),
                                    WebSocketMessageType.Text,
                                    true,
                                    sessionCts.Token);

                                Console.WriteLine($"[{clientId}] 服务器 -> 客户端: {cleanedMessage}");
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
        /// 清理ASR响应消息中的特殊标记
        /// </summary>
        /// <param name="message">原始ASR响应消息</param>
        /// <returns>清理后的消息</returns>
        private string CleanAsrResponseMessage(string message)
        {
            try
            {
                // 直接使用正则表达式替换所有<|xxx|>标记
                return Regex.Replace(message, "<\\|[^|]*\\|>", "");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"清理ASR消息时出错: {ex.Message}");
                return message; // 出错时返回原始消息
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

            // 用于累积WAV数据块的临时缓冲区
            MemoryStream pendingAudioData = new MemoryStream();
            bool isProcessingWavFile = false;
            DateTime lastChunkTime = DateTime.Now;
            const int inactivityTimeoutMs = 1000; // 1秒的超时，检测音频流结束

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

                        // 处理控制消息
                        if (message.Contains("\"is_speaking\": true") || message.Contains("\"is_speaking\":true"))
                        {
                            // 开始新的音频流
                            session.AudioBuffer.SetLength(0); // 清空缓冲区
                            session.IsReceivingAudio = true;
                            pendingAudioData.SetLength(0); // 清空临时缓冲区
                            isProcessingWavFile = false;
                            Console.WriteLine($"[{clientId}] 开始接收新的音频流");
                        }
                        else if (message.Contains("\"is_speaking\": false") || message.Contains("\"is_speaking\":false"))
                        {
                            // 音频流结束，处理临时缓冲区中的数据
                            if (pendingAudioData.Length > 0)
                            {
                                Console.WriteLine($"[{clientId}] 处理临时缓冲区中的音频数据: {pendingAudioData.Length} 字节");
                                await ProcessAndSendAudioData(clientId, serverClient, pendingAudioData.ToArray());
                                pendingAudioData.SetLength(0);
                            }

                            // 如果之前设置了接收状态，处理累积的数据
                            if (session.IsReceivingAudio && session.AudioBuffer.Length > 0)
                            {
                                Console.WriteLine($"[{clientId}] 音频接收完成，长度: {session.AudioBuffer.Length} 字节，开始处理...");
                                await ProcessCompleteAudioData(clientId, session);
                                session.IsReceivingAudio = false;
                            }
                        }

                        // 发送文本消息
                        serverClient.SendTextMessage(message);
                    }
                    else if (receiveResult.MessageType == WebSocketMessageType.Binary)
                    {
                        // 判断是否为WAV文件（检查RIFF头）
                        bool isWavData = receivedData.Length >= 4 &&
                                        receivedData[0] == 0x52 && receivedData[1] == 0x49 &&
                                        receivedData[2] == 0x46 && receivedData[3] == 0x46;

                        // 自动检测模式：如果是WAV头，开始处理
                        if (isWavData && !isProcessingWavFile)
                        {
                            isProcessingWavFile = true;
                            Console.WriteLine($"[{clientId}] 自动检测到WAV文件头，开始收集音频数据");
                            // 清空之前的数据
                            pendingAudioData.SetLength(0);
                        }

                        // 当前是处理WAV文件模式
                        if (isProcessingWavFile)
                        {
                            lastChunkTime = DateTime.Now;
                            pendingAudioData.Write(receivedData, 0, receivedData.Length);
                            Console.WriteLine($"[{clientId}] 累积WAV数据: {receivedData.Length} 字节，总计: {pendingAudioData.Length} 字节");

                            // 检查是否接收完成（基于超时机制）
                            if ((DateTime.Now - lastChunkTime).TotalMilliseconds > inactivityTimeoutMs)
                            {
                                Console.WriteLine($"[{clientId}] 检测到音频传输完成（超时），处理累积的WAV数据: {pendingAudioData.Length} 字节");
                                if (pendingAudioData.Length > 0)
                                {
                                    await ProcessAndSendAudioData(clientId, serverClient, pendingAudioData.ToArray());
                                    pendingAudioData.SetLength(0);
                                    isProcessingWavFile = false;
                                }
                            }
                            continue; // 跳过直接发送
                        }

                        // 如果客户端使用标准协议进入了接收状态
                        if (session.IsReceivingAudio)
                        {
                            // 将接收到的音频数据添加到缓冲区
                            session.AudioBuffer.Write(receivedData, 0, receivedData.Length);
                            session.LastAudioReceiveTime = DateTime.Now;

                            Console.WriteLine($"[{clientId}] 接收到二进制数据: {receivedData.Length} 字节，累计: {session.AudioBuffer.Length} 字节");

                            // 检查缓冲区是否过大，防止内存溢出
                            if (session.AudioBuffer.Length > 20 * 1024 * 1024) // 20MB限制
                            {
                                Console.WriteLine($"[{clientId}] 音频缓冲区过大({session.AudioBuffer.Length}字节)，提前处理");
                                await ProcessCompleteAudioData(clientId, session);
                                session.AudioBuffer.SetLength(0);
                            }
                        }
                        else
                        {
                            // 直接发送模式（兼容现有协议）
                            Console.WriteLine($"[{clientId}] 接收到二进制数据，直接发送: {receivedData.Length} 字节");
                            serverClient.SendBinaryData(receivedData);
                        }
                    }

                    // 检查WAV处理是否超时（在消息循环的每次迭代中）
                    if (isProcessingWavFile && (DateTime.Now - lastChunkTime).TotalMilliseconds > inactivityTimeoutMs)
                    {
                        Console.WriteLine($"[{clientId}] 检测到音频传输完成（超时），处理累积的WAV数据: {pendingAudioData.Length} 字节");
                        if (pendingAudioData.Length > 0)
                        {
                            await ProcessAndSendAudioData(clientId, serverClient, pendingAudioData.ToArray());
                            pendingAudioData.SetLength(0);
                            isProcessingWavFile = false;
                        }
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
                Console.WriteLine($"[{clientId}] 错误详情: {ex}");
            }
            finally
            {
                // 确保关闭会话
                await CloseSession(clientId);
                Console.WriteLine($"[{clientId}] 客户端会话已关闭");

                // 释放临时缓冲区
                pendingAudioData.Dispose();
            }
        }

        /// <summary>
        /// 处理并发送音频数据
        /// </summary>
        /// <param name="clientId">客户端ID</param>
        /// <param name="client">WebSocket客户端</param>
        /// <param name="audioData">音频数据</param>
        private async Task ProcessAndSendAudioData(string clientId, CWebSocketClient client, byte[] audioData)
        {
            try
            {
                if (audioData.Length < 44)
                {
                    // 数据太少，可能不是有效的WAV文件
                    Console.WriteLine($"[{clientId}] 音频数据太短 ({audioData.Length} 字节)，不足以包含WAV头部(44字节)，直接发送原始数据");
                    client.SendBinaryData(audioData);
                    return;
                }

                // 检查是否是WAV文件
                if (audioData[0] == 0x52 && audioData[1] == 0x49 &&
                    audioData[2] == 0x46 && audioData[3] == 0x46)
                {
                    Console.WriteLine($"[{clientId}] 检测到WAV文件头: RIFF");

                    try
                    {
                        // 尝试读取WAV头部中的采样率信息（字节24-27）
                        byte[] sampleRateBytes = new byte[4];
                        Array.Copy(audioData, 24, sampleRateBytes, 0, 4);
                        int rawSampleRate = BitConverter.ToInt32(sampleRateBytes, 0);
                        Console.WriteLine($"[{clientId}] 从WAV头部读取的原始采样率: {rawSampleRate} Hz");

                        int detectedSampleRate = AudioResampler.GetWavSampleRate(audioData);
                        Console.WriteLine($"[{clientId}] AudioResampler检测到的采样率: {detectedSampleRate} Hz");

                        if (detectedSampleRate != TargetSampleRate)
                        {
                            Console.WriteLine($"[{clientId}] 检测到WAV数据采样率为 {detectedSampleRate}Hz，需要转换为 {TargetSampleRate}Hz");

                            // 重采样完整数据
                            byte[] resampledData = AudioResampler.ResampleWavData(audioData, TargetSampleRate);

                            // 发送重采样后的数据
                            Console.WriteLine($"[{clientId}] 重采样完成，原始大小: {audioData.Length} 字节, 重采样后: {resampledData.Length} 字节");
                            client.SendBinaryData(resampledData);
                            return;
                        }
                        else
                        {
                            Console.WriteLine($"[{clientId}] WAV数据采样率已经是 {detectedSampleRate}Hz，无需转换");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{clientId}] 检测或重采样WAV出错: {ex.Message}");
                        Console.WriteLine($"[{clientId}] 错误详情: {ex}");
                    }
                }
                else
                {
                    // 打印前8个字节，用于调试
                    string headerHex = BitConverter.ToString(audioData, 0, Math.Min(8, audioData.Length));
                    Console.WriteLine($"[{clientId}] 数据头部不是WAV格式(RIFF): {headerHex}");
                }

                // 如果不需要重采样或不是WAV文件，直接发送原始数据
                Console.WriteLine($"[{clientId}] 发送原始音频数据，长度: {audioData.Length} 字节");
                client.SendBinaryData(audioData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{clientId}] 处理音频数据时出错: {ex.Message}");
                Console.WriteLine($"[{clientId}] 错误详情: {ex}");

                // 出错时尝试发送原始数据
                Console.WriteLine($"[{clientId}] 发送原始音频数据（错误恢复），长度: {audioData.Length} 字节");
                client.SendBinaryData(audioData);
            }
        }

        /// <summary>
        /// 处理完整的音频数据
        /// </summary>
        /// <param name="clientId">客户端ID</param>
        /// <param name="session">客户端会话</param>
        private async Task ProcessCompleteAudioData(string clientId, ClientSession session)
        {
            try
            {
                // 获取完整的音频数据
                session.AudioBuffer.Position = 0; // 重置流位置到开始
                byte[] completeData = session.AudioBuffer.ToArray();

                await ProcessAndSendAudioData(clientId, session.ServerClient, completeData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{clientId}] 处理完整音频数据时出错: {ex.Message}");
                Console.WriteLine($"[{clientId}] 错误详情: {ex}");

                // 出错时尝试发送原始数据
                if (session.AudioBuffer.Length > 0)
                {
                    session.AudioBuffer.Position = 0; // 重置流位置到开始
                    byte[] originalData = session.AudioBuffer.ToArray();
                    Console.WriteLine($"[{clientId}] 发送原始音频数据（错误恢复），长度: {originalData.Length} 字节");
                    session.ServerClient.SendBinaryData(originalData);
                }
            }
        }
    }
}