using System;
using System.Threading;
using System.Threading.Tasks;

namespace MXASRServer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("MXASR WebSocket服务器");
            Console.WriteLine("----------------------------");

            string host = "0.0.0.0";   // 监听所有网络接口
            int port = 9096;         // 默认端口
            string targetUrl = "ws://124.223.76.169:10096/";  // 默认目标服务器

            // 创建取消令牌源
            using var cts = new CancellationTokenSource();

            // 注册Ctrl+C事件处理
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            // 创建WebSocket服务器
            CWebSocketServer server = new CWebSocketServer(targetUrl);

            try
            {
                Console.WriteLine($"正在启动服务器在 {host}:{port}...");
                await server.StartAsync(host, port);
                Console.WriteLine($"服务器已成功启动在 {host}:{port}!");
                Console.WriteLine("按Ctrl+C停止服务器...");

                // 保持程序运行直到收到取消信号
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(1000, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("\n正在停止服务器...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"启动服务器时出错: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"详细错误: {ex.InnerException.Message}");
                }
            }
            finally
            {
                await server.StopAsync();
                Console.WriteLine("服务器已停止。");
            }
        }
    }
}