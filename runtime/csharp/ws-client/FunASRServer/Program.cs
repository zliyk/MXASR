using System;
using System.Threading.Tasks;

namespace FunASRServer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("FunASR WebSocket代理服务器");
            Console.WriteLine("----------------------------");

            string host = "0.0.0.0";  // 默认监听所有网络接口
            int port = 10095;         // 默认端口
            string targetUrl = "ws://124.223.76.169:10095/";  // 默认目标服务器

            // 解析命令行参数
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--host" && i + 1 < args.Length)
                {
                    host = args[i + 1];
                    i++;
                }
                else if (args[i] == "--port" && i + 1 < args.Length)
                {
                    if (int.TryParse(args[i + 1], out int p))
                    {
                        port = p;
                    }
                    i++;
                }
                else if (args[i] == "--target" && i + 1 < args.Length)
                {
                    targetUrl = args[i + 1];
                    i++;
                }
            }

            // 创建并启动WebSocket代理服务器
            CWebSocketServer server = new CWebSocketServer(targetUrl);

            try
            {
                await server.StartAsync(host, port);

                Console.WriteLine("服务器已启动。按Enter键停止服务器...");
                Console.ReadLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"启动服务器时出错: {ex.Message}");
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