using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fleck;
using System.Collections.Generic;

namespace NewServerTest
{
    // 定义响应类型，用于JSON序列化
    [JsonSerializable(typeof(BinaryResponse))]
    public partial class BinaryResponseContext : JsonSerializerContext
    {
    }

    public class BinaryResponse
    {
        public string Status { get; set; } = "success";
        public int ReceivedBytes { get; set; }
        public string Timestamp { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public class MXServer
    {
        private WebSocketServer _server;
        private List<IWebSocketConnection> _allSockets;
        private Random _random;

        public MXServer(string url)
        {
            _server = new WebSocketServer(url);
            _allSockets = new List<IWebSocketConnection>();
            _random = new Random();
        }

        public void Start()
        {
            _server.Start(socket =>
            {
                socket.OnOpen = () =>
                {
                    Console.WriteLine("[服务端] 连接已打开");
                    _allSockets.Add(socket);
                };

                socket.OnClose = () =>
                {
                    Console.WriteLine("[服务端] 连接已关闭");
                    _allSockets.Remove(socket);
                };

                socket.OnMessage = message =>
                {
                    Console.WriteLine($"[服务端] 接收到文本消息: {message}");
                    // 发送随机字符串作为响应
                    string randomResponse = GenerateRandomString(20);
                    Console.WriteLine($"[服务端] 发送随机字符串响应: {randomResponse}");
                    socket.Send(randomResponse);
                };

                socket.OnBinary = bytes =>
                {
                    Console.WriteLine($"[服务端] 接收到二进制消息: {bytes.Length} 字节");
                    // 发送JSON响应
                    var response = new BinaryResponse
                    {
                        Status = "success",
                        ReceivedBytes = bytes.Length,
                        Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        Message = "已成功接收字节流数据"
                    };

                    // 使用源生成的序列化上下文
                    string jsonString = JsonSerializer.Serialize(response, BinaryResponseContext.Default.BinaryResponse);
                    Console.WriteLine($"[服务端] 发送JSON响应: {jsonString}");
                    socket.Send(jsonString);
                };

                socket.OnError = exception =>
                {
                    Console.WriteLine($"[服务端] 错误: {exception.Message}");
                };
            });

            Console.WriteLine($"[服务端] 服务器已启动，监听地址: {_server.Location}");
        }

        public void Stop()
        {
            // 关闭所有连接
            foreach (var socket in _allSockets)
            {
                socket.Close();
            }
            _allSockets.Clear();
            _server.Dispose();
            Console.WriteLine("[服务端] 服务器已停止");
        }

        private string GenerateRandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var stringChars = new char[length];

            for (int i = 0; i < length; i++)
            {
                stringChars[i] = chars[_random.Next(chars.Length)];
            }

            return new string(stringChars);
        }
    }
}