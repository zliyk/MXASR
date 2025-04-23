using System;
using System.Threading.Tasks;

namespace MXASRServer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("MXASR WebSocket服务器");
            Console.WriteLine("----------------------------");

            string host = "0.0.0.0";  // 改为默认只监听本地地址，避免权限问题
            int port = 9095;         // 默认端口
            string targetUrl = "ws://124.223.76.169:10095/";  // 默认目标服务器


            // 创建并启动WebSocket服务器
            CWebSocketServer server = new CWebSocketServer(targetUrl);

            try
            {
                await server.StartAsync(host, port);

                Console.WriteLine("服务器已成功启动!");
                Console.WriteLine("按Enter键停止服务器...");
                Console.ReadLine();
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
            }

            Console.WriteLine("服务器已停止。按任意键退出...");
            Console.ReadKey();
        }

    }
}