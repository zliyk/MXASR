using System.Collections.Specialized;
using WebSocketSpace;

namespace FunASRWSClient_Offline
{
    /// <summary>
    /// /主程序入口
    /// </summary>
    public class Program
    {
        private static void Main()
        {
            WSClient_Offline m_funasrclient = new WSClient_Offline();
            m_funasrclient.FunASR_Main();
        }
    }

    public class WSClient_Offline
    {
        private static CWebSocketClient m_websocketclient = new CWebSocketClient();
        public async void FunASR_Main()
        {
            loadhotword();
            //初始化通信连接
            string errorStatus = string.Empty;
            string commstatus = ClientConnTest();
            if (commstatus != "通信连接成功")
                errorStatus = commstatus;
            //程序初始监测异常--报错、退出
            if (errorStatus != string.Empty)
            {
                //报错方式待加
                Environment.Exit(0);
            }

            //循环输入推理文件
            while (true)
            {
                Console.WriteLine("请输入转录文件路径：");
                string filepath = Console.ReadLine();
                if (filepath != string.Empty && filepath != null)
                {
                    await m_websocketclient.ClientSendFileFunc(filepath);
                }
            }
        }
        static void loadhotword()
        {
            //string filePath = "hotword.txt";
            //try
            //{
            //    // 使用 StreamReader 打开文本文件
            //    using (StreamReader sr = new StreamReader(filePath))
            //    {
            //        string line;
            //        // 逐行读取文件内容
            //        while ((line = sr.ReadLine()) != null)
            //        {
            //            hotword += line;
            //            hotword += " ";
            //        }
            //    }
            //}
            //catch (Exception ex)
            //{
            //    Console.WriteLine("读取文件时发生错误：" + ex.Message);
            //}
            //finally
            //{
            //    if (hotword.Length > 0 && hotword[hotword.Length - 1] == ' ')
            //        hotword = hotword.Substring(0, hotword.Length - 1);
            //}
        }
        private static string ClientConnTest()
        {
            //WebSocket连接状态监测
            Task<string> websocketstatus = m_websocketclient.ClientConnTest();
            if (websocketstatus != null && websocketstatus.Result.IndexOf("成功") == -1)
                return websocketstatus.Result;
            return "通信连接成功";
        }
    }
}