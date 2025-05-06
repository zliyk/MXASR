namespace NewServerTest
{
    internal class Program
    {
        private static CancellationTokenSource _serverCancellation = new CancellationTokenSource();

        static async Task Main(string[] args)
        {
            // 启动服务器线程
            Task serverTask = Task.Run(async () => await RunServer(_serverCancellation.Token));

            // 等待服务器启动
            await Task.Delay(1000);

            // 主线程运行客户端
            await RunClient();

            // 停止服务器
            _serverCancellation.Cancel();
            try
            {
                await serverTask;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[客户端] 程序已退出，服务器已停止");
            }
        }

        static async Task RunServer(CancellationToken cancellationToken)
        {
            try
            {
                // 启动服务器模式
                var server = new MXServer("ws://0.0.0.0:10095");
                server.Start();

                // 等待取消信号
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(100, cancellationToken);
                }

                // 停止服务器
                server.Stop();
            }
            catch (OperationCanceledException)
            {
                // 忽略取消异常
            }
        }

        static async Task RunClient()
        {
            bool exit = false;

            while (!exit)
            {
                Console.WriteLine("\n[客户端] 请选择操作：");
                Console.WriteLine("1. 发送文本消息");
                Console.WriteLine("2. 发送二进制数据");
                Console.WriteLine("0. 退出程序");

                string? choice = Console.ReadLine();

                switch (choice)
                {
                    case "1":
                        await SendTextMessage();
                        break;
                    case "2":
                        await SendBinaryData();
                        break;
                    case "0":
                        exit = true;
                        break;
                    default:
                        Console.WriteLine("[客户端] 无效的选择");
                        break;
                }
            }
        }

        static async Task SendTextMessage()
        {
            Console.WriteLine("[客户端] 请输入要发送的文本消息：");
            string? message = Console.ReadLine();

            if (string.IsNullOrEmpty(message))
            {
                Console.WriteLine("[客户端] 消息不能为空");
                return;
            }

            // 客户端测试模式（文本）
            MXClient client = new MXClient();
            try
            {
                Console.WriteLine("[客户端] 正在连接服务器...");
                await client.ConnectAsync("ws://127.0.0.1:10095");

                Console.WriteLine($"[客户端] 发送消息: {message}");
                await client.SendMessageAsync(message);

                string response = await client.ReceiveMessageAsync();
                Console.WriteLine($"[客户端] 收到服务器响应: {response}");

                await client.CloseAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[客户端] 错误: {ex.Message}");
            }
        }

        static async Task SendBinaryData()
        {
            Console.WriteLine("[客户端] 请输入要发送的二进制数据大小（字节数）：");
            if (!int.TryParse(Console.ReadLine(), out int size) || size <= 0)
            {
                Console.WriteLine("[客户端] 无效的大小");
                return;
            }

            // 客户端测试模式（二进制）
            MXClient client = new MXClient();
            try
            {
                Console.WriteLine("[客户端] 正在连接服务器...");
                await client.ConnectAsync("ws://127.0.0.1:10095");

                // 创建测试二进制数据
                byte[] binaryData = new byte[size];
                Random random = new Random();
                random.NextBytes(binaryData);

                Console.WriteLine($"[客户端] 发送二进制数据，共 {binaryData.Length} 字节");
                await client.SendBinaryAsync(binaryData);

                string response = await client.ReceiveMessageAsync();
                Console.WriteLine($"[客户端] 收到服务器响应: {response}");

                await client.CloseAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[客户端] 错误: {ex.Message}");
            }
        }
    }
}
