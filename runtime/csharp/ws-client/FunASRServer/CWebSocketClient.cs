using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Websocket.Client;
using System.Reactive.Linq;

namespace FunASRServer
{
    /// <summary>
    /// WebSocket客户端，用于连接到FunASR服务器
    /// </summary>
    public class CWebSocketClient
    {
        private readonly Uri _serverUri;
        private WebsocketClient _client;
        private bool _isConnected = false;
        private Action<string> _messageHandler;

        /// <summary>
        /// 创建WebSocket客户端
        /// </summary>
        /// <param name="serverUrl">FunASR服务器URL</param>
        public CWebSocketClient(string serverUrl = "ws://124.223.76.169:10096/")
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
    }
}