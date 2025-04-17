using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Websocket.Client;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Linq;

namespace FunASRServer
{
    /// <summary>
    /// WebSocket客户端，用于连接到FunASR服务器
    /// </summary>
    public class CWebSocketClient
    {
        public string hotword = null;
        private readonly Uri _serverUri;
        private WebsocketClient _client;
        private bool _isConnected = false;
        private Action<string> _messageHandler;

        /// <summary>
        /// 创建WebSocket客户端
        /// </summary>
        /// <param name="serverUrl">FunASR服务器URL</param>
        public CWebSocketClient(string serverUrl = "ws://124.223.76.169:10095/")
        {
            _serverUri = new Uri(serverUrl);
            _client = new WebsocketClient(_serverUri);
            _messageHandler = (message) => Console.WriteLine(message);
        }

        /// <summary>
        /// 设置消息处理回调
        /// </summary>
        /// <param name="handler">消息处理函数</param>
        public void SetMessageHandler(Action<string> handler)
        {
            _messageHandler = handler ?? ((message) => Console.WriteLine(message));
        }

        /// <summary>
        /// 发送文本消息到FunASR服务器
        /// </summary>
        /// <param name="message">要发送的文本消息</param>
        public void SendTextMessage(string message)
        {
            if (_client != null && _client.IsRunning)
            {
                _client.Send(message);
            }
        }

        /// <summary>
        /// 发送二进制数据到FunASR服务器
        /// </summary>
        /// <param name="data">要发送的二进制数据</param>
        public void SendBinaryData(byte[] data)
        {
            if (_client != null && _client.IsRunning)
            {
                _client.Send(data);
            }
        }

        /// <summary>
        /// 连接测试，建立与FunASR服务器的WebSocket连接
        /// </summary>
        /// <returns>连接状态</returns>
        public async Task<string> ClientConnTest()
        {
            string commstatus = "WebSocket通信连接失败";
            try
            {
                if (_isConnected)
                {
                    return "WebSocket通信连接成功";
                }

                _client = new WebsocketClient(_serverUri);
                _client.Name = "funasr";
                _client.ReconnectTimeout = null;

                _client.ReconnectionHappened.Subscribe(info =>
                    Console.WriteLine($"重新连接成功, 类型: {info.Type}, url: {_client.Url}"));

                _client.DisconnectionHappened.Subscribe(info =>
                    Console.WriteLine($"连接断开, 类型: {info.Type}"));

                _client
                    .MessageReceived
                    .Where(msg => msg.Text != null)
                    .Subscribe(msg =>
                    {
                        recmessage(msg.Text);
                    });

                await _client.Start();

                if (_client.IsRunning)
                {
                    _isConnected = true;
                    commstatus = "WebSocket通信连接成功";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"连接服务器时出错: {ex}");
                _client?.Dispose();
                _isConnected = false;
            }

            return commstatus;
        }

        /// <summary>
        /// 断开与FunASR服务器的连接
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                if (_client != null && _client.IsRunning)
                {
                    await _client.Stop(WebSocketCloseStatus.NormalClosure, "客户端主动断开");
                    _client.Dispose();
                }
                _isConnected = false;
                Console.WriteLine("已断开与FunASR服务器的连接");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"断开连接时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 接收处理来自FunASR服务器的消息
        /// </summary>
        /// <param name="message">接收到的消息</param>
        public void recmessage(string message)
        {
            if (message != null)
            {
                // 调用消息处理回调
                _messageHandler(message);
            }
        }

        /// <summary>
        /// 确保已连接到FunASR服务器
        /// </summary>
        /// <returns>是否连接成功</returns>
        private async Task<bool> EnsureConnectedAsync()
        {
            try
            {
                if (_client == null || !_client.IsRunning)
                {
                    string status = await ClientConnTest();
                    return status == "WebSocket通信连接成功";
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        // 发送离线识别请求
        public async Task<string> SendOfflineRequestAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return JsonSerializer.Serialize(new { status = "error", message = $"文件不存在: {filePath}" });
            }

            try
            {
                if (!_client.IsRunning && !await EnsureConnectedAsync())
                {
                    return JsonSerializer.Serialize(new { status = "error", message = "未连接到服务器" });
                }

                string fileExtension = Path.GetExtension(filePath).Replace(".", "").ToLower();

                if (!(fileExtension == "mp3" || fileExtension == "mp4" || fileExtension == "wav" || fileExtension == "pcm"))
                {
                    return JsonSerializer.Serialize(new { status = "error", message = "不支持的音频格式，仅支持mp3/mp4/wav/pcm" });
                }

                // 设置结果等待事件
                var resultReceived = new TaskCompletionSource<string>();
                var originalHandler = _messageHandler;

                // 临时修改消息处理器以捕获最终结果
                _messageHandler = (msg) =>
                {
                    Console.WriteLine($"收到服务器响应: {msg}");

                    // 检查是否是最终结果消息
                    if (msg.Contains("\"is_final\":true") || msg.Contains("\"mode\":\"offline\""))
                    {
                        resultReceived.TrySetResult(msg);
                    }

                    // 同时也调用原始处理器
                    originalHandler(msg);
                };

                // 发送请求
                if (fileExtension == "wav")
                {
                    string firstbuff = string.Format("{{\"mode\": \"offline\", \"wav_name\": \"{0}\", \"is_speaking\": true, \"hotwords\":\"{1}\"}}",
                        Path.GetFileName(filePath), hotword);
                    _client.Send(firstbuff);
                    await SendWAVFile(_client, filePath, true);
                }
                else
                {
                    string firstbuff = string.Format("{{\"mode\": \"offline\", \"wav_name\": \"{0}\", \"is_speaking\": true, \"hotwords\":\"{1}\", \"wav_format\":\"{2}\"}}",
                        Path.GetFileName(filePath), hotword, fileExtension);
                    _client.Send(firstbuff);
                    await SendWAVFile(_client, filePath, false);
                }

                // 等待结果或超时
                Task timeoutTask = Task.Delay(60000); // 60秒超时
                Task completedTask = await Task.WhenAny(resultReceived.Task, timeoutTask);

                // 恢复原始消息处理器
                _messageHandler = originalHandler;

                if (completedTask == timeoutTask)
                {
                    return JsonSerializer.Serialize(new { status = "error", message = "请求超时" });
                }

                return resultReceived.Task.Result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发送离线请求时出错: {ex.Message}");
                return JsonSerializer.Serialize(new { status = "error", message = ex.Message });
            }
        }

        // 开始实时识别
        public async Task<string> StartRealtimeRecognitionAsync()
        {
            if (!_client.IsRunning && !await EnsureConnectedAsync())
            {
                return JsonSerializer.Serialize(new { status = "error", message = "未连接到服务器" });
            }

            try
            {
                string request = string.Format("{{\"mode\": \"online\", \"is_speaking\": true, \"hotwords\":\"{0}\"}}", hotword);
                _client.Send(request);

                return JsonSerializer.Serialize(new { status = "ok", message = "实时转写已开始" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"开始实时识别时出错: {ex.Message}");
                return JsonSerializer.Serialize(new { status = "error", message = ex.Message });
            }
        }

        // 停止实时识别
        public async Task<string> StopRealtimeRecognitionAsync()
        {
            if (!_client.IsRunning)
            {
                return JsonSerializer.Serialize(new { status = "error", message = "未连接到服务器" });
            }

            try
            {
                _client.Send("{\"is_speaking\": false}");
                return JsonSerializer.Serialize(new { status = "ok", message = "实时转写已停止" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"停止实时识别时出错: {ex.Message}");
                return JsonSerializer.Serialize(new { status = "error", message = ex.Message });
            }
        }

        // 发送音频数据
        public async Task<string> SendAudioDataAsync(byte[] audioData)
        {
            if (!_client.IsRunning && !await EnsureConnectedAsync())
            {
                return JsonSerializer.Serialize(new { status = "error", message = "未连接到服务器" });
            }

            try
            {
                _client.Send(audioData);
                return JsonSerializer.Serialize(new { status = "ok", message = "音频数据已发送" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发送音频数据时出错: {ex.Message}");
                return JsonSerializer.Serialize(new { status = "error", message = ex.Message });
            }
        }

        // 发送WAV文件
        private async Task SendWAVFile(WebsocketClient client, string filePath, bool skipHeader)
        {
            byte[] fileBytes = await File.ReadAllBytesAsync(filePath);

            // 如果是WAV文件且需要跳过头部，则跳过44字节的WAV头
            if (skipHeader)
            {
                fileBytes = fileBytes.Skip(44).ToArray();
            }

            // 分块发送文件
            int chunkSize = 1024000; // 约1MB的块大小
            for (int i = 0; i < fileBytes.Length; i += chunkSize)
            {
                byte[] chunk = fileBytes.Skip(i).Take(Math.Min(chunkSize, fileBytes.Length - i)).ToArray();
                client.Send(chunk);
                await Task.Delay(5); // 短暂延迟，避免发送过快
            }

            // 发送结束标志
            await Task.Delay(10);
            client.Send("{\"is_speaking\": false}");
        }
    }
}