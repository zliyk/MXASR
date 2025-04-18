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

            string host = "127.0.0.1";  // 改为默认只监听本地地址，避免权限问题
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
                else if (args[i] == "--help" || args[i] == "-h")
                {
                    ShowHelp();
                    return;
                }
            }

            Console.WriteLine("启动参数:");
            Console.WriteLine($"  主机: {host}");
            Console.WriteLine($"  端口: {port}");
            Console.WriteLine($"  目标服务器: {targetUrl}");
            Console.WriteLine();

            if (host == "0.0.0.0" || host == "+")
            {
                Console.WriteLine("警告: 监听所有网络接口需要管理员权限！");
                Console.WriteLine("如果不是管理员权限运行，请尝试使用 --host 127.0.0.1 只监听本地连接");
                Console.WriteLine();
            }

            // 创建并启动WebSocket代理服务器
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

                // 提供常见问题解决方案
                Console.WriteLine("\n可能的解决方案:");
                Console.WriteLine("1. 如果是权限问题，请以管理员身份运行程序");
                Console.WriteLine("2. 或者执行以下命令来授予URL访问权限:");
                Console.WriteLine($"   netsh http add urlacl url=http://{host}:{port}/ws/ user=Everyone");
                Console.WriteLine("3. 如果端口被占用，请使用 --port 参数指定其他端口");
                Console.WriteLine("4. 使用 --host 127.0.0.1 只监听本地连接，可能不需要管理员权限");
                Console.WriteLine("\n使用 --help 查看更多信息");
            }
            finally
            {
                await server.StopAsync();
            }

            Console.WriteLine("服务器已停止。按任意键退出...");
            Console.ReadKey();
        }

        static void ShowHelp()
        {
            Console.WriteLine("FunASR WebSocket代理服务器帮助");
            Console.WriteLine("----------------------------");
            Console.WriteLine("用法: FunASRServer.exe [选项]");
            Console.WriteLine();
            Console.WriteLine("选项:");
            Console.WriteLine("  --host <主机>     指定监听的主机地址 (默认: 127.0.0.1)");
            Console.WriteLine("                    使用 0.0.0.0 监听所有网络接口(需要管理员权限)");
            Console.WriteLine("  --port <端口>     指定监听的端口 (默认: 10095)");
            Console.WriteLine("  --target <URL>    指定目标FunASR服务器URL");
            Console.WriteLine("                    (默认: ws://124.223.76.169:10095/)");
            Console.WriteLine("  --help, -h        显示此帮助信息");
            Console.WriteLine();
            Console.WriteLine("示例:");
            Console.WriteLine("  FunASRServer.exe --host 127.0.0.1 --port 10095");
            Console.WriteLine("  FunASRServer.exe --host 0.0.0.0 --port 8080 --target ws://your-server:10095/");
            Console.WriteLine();
            Console.WriteLine("注意:");
            Console.WriteLine("  监听0.0.0.0或+等通配符地址需要管理员权限或配置URL ACL权限");
            Console.WriteLine("  可以使用以下命令配置URL ACL权限:");
            Console.WriteLine("  netsh http add urlacl url=http://+:10095/ws/ user=Everyone");
        }
    }
}