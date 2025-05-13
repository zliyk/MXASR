using System;
using System.IO;
using System.Text;
using System.Threading;

namespace MXASRServer
{
    /// <summary>
    /// 日志记录类，支持输出到控制台和文件
    /// </summary>
    public static class Logger
    {
        private static readonly object _lock = new object();
        private static StreamWriter _logFileWriter = null;
        private static string _logFilePath = null;

        // 日志级别
        public enum LogLevel
        {
            Debug,
            Info,
            Warning,
            Error
        }

        // 当前日志级别
        public static LogLevel CurrentLogLevel { get; set; } = LogLevel.Info;

        // 汇总日志信息的频率（累积多少条后输出一次汇总）
        private static int _summaryFrequency = 100;

        // 是否启用日志
        public static bool Enabled { get; set; } = true;

        // 是否同时输出到控制台
        public static bool ConsoleOutput { get; set; } = true;

        // 日志汇总信息缓存
        private static class SummaryCache
        {
            public static int AudioChunkCount = 0;
            public static long AudioTotalBytes = 0;
            public static string ClientId = null;
            public static readonly object LockObj = new object();
        }

        /// <summary>
        /// 初始化日志
        /// </summary>
        /// <param name="logFilePath">日志文件路径，null表示使用默认路径</param>
        /// <param name="logLevel">日志级别</param>
        /// <param name="consoleOutput">是否同时输出到控制台</param>
        /// <param name="summaryFrequency">汇总日志频率</param>
        public static void Initialize(string logFilePath = null, LogLevel logLevel = LogLevel.Info, bool consoleOutput = true, int summaryFrequency = 100)
        {
            try
            {
                CurrentLogLevel = logLevel;
                ConsoleOutput = consoleOutput;
                _summaryFrequency = summaryFrequency;

                if (string.IsNullOrEmpty(logFilePath))
                {
                    // 默认日志路径: logs/mxasr_yyyyMMdd.log
                    string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                    if (!Directory.Exists(logDirectory))
                    {
                        Directory.CreateDirectory(logDirectory);
                    }

                    _logFilePath = Path.Combine(logDirectory, $"mxasr_{DateTime.Now:yyyyMMdd}.log");
                }
                else
                {
                    _logFilePath = logFilePath;

                    // 确保日志目录存在
                    string logDirectory = Path.GetDirectoryName(_logFilePath);
                    if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
                    {
                        Directory.CreateDirectory(logDirectory);
                    }
                }

                lock (_lock)
                {
                    // 关闭之前的日志文件
                    if (_logFileWriter != null)
                    {
                        _logFileWriter.Flush();
                        _logFileWriter.Close();
                        _logFileWriter.Dispose();
                    }

                    // 创建新的日志文件，追加模式
                    _logFileWriter = new StreamWriter(_logFilePath, true, Encoding.UTF8)
                    {
                        AutoFlush = true
                    };
                }

                Enabled = true;

                // 记录启动日志
                Info("==================================================");
                Info($"日志系统初始化完成，级别：{CurrentLogLevel}，文件：{_logFilePath}");
                Info("==================================================");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"初始化日志系统失败: {ex.Message}");
                Enabled = false;
            }
        }

        /// <summary>
        /// 关闭日志
        /// </summary>
        public static void Close()
        {
            lock (_lock)
            {
                if (_logFileWriter != null)
                {
                    _logFileWriter.Flush();
                    _logFileWriter.Close();
                    _logFileWriter.Dispose();
                    _logFileWriter = null;
                }
            }

            Enabled = false;
        }

        /// <summary>
        /// 写入调试日志
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void Debug(string message)
        {
            if (Enabled && CurrentLogLevel <= LogLevel.Debug)
            {
                WriteLog("DEBUG", message);
            }
        }

        /// <summary>
        /// 写入信息日志
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void Info(string message)
        {
            if (Enabled && CurrentLogLevel <= LogLevel.Info)
            {
                WriteLog("INFO", message);
            }
        }

        /// <summary>
        /// 写入警告日志
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void Warning(string message)
        {
            if (Enabled && CurrentLogLevel <= LogLevel.Warning)
            {
                WriteLog("WARN", message);
            }
        }

        /// <summary>
        /// 写入错误日志
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void Error(string message)
        {
            if (Enabled && CurrentLogLevel <= LogLevel.Error)
            {
                WriteLog("ERROR", message);
            }
        }

        /// <summary>
        /// 写入带客户端ID的信息日志
        /// </summary>
        /// <param name="clientId">客户端ID</param>
        /// <param name="message">日志消息</param>
        public static void ClientInfo(string clientId, string message)
        {
            if (Enabled && CurrentLogLevel <= LogLevel.Info)
            {
                WriteLog("INFO", $"[{clientId}] {message}");
            }
        }

        /// <summary>
        /// 写入带客户端ID的错误日志
        /// </summary>
        /// <param name="clientId">客户端ID</param>
        /// <param name="message">日志消息</param>
        public static void ClientError(string clientId, string message)
        {
            if (Enabled && CurrentLogLevel <= LogLevel.Error)
            {
                WriteLog("ERROR", $"[{clientId}] {message}");
            }
        }

        /// <summary>
        /// 记录音频数据块信息，按频率汇总输出
        /// </summary>
        /// <param name="clientId">客户端ID</param>
        /// <param name="chunkSize">数据块大小</param>
        /// <param name="totalSize">累计大小</param>
        public static void AudioChunk(string clientId, int chunkSize, long totalSize)
        {
            if (!Enabled || CurrentLogLevel > LogLevel.Debug)
                return;

            lock (SummaryCache.LockObj)
            {
                // 如果是新的客户端，输出之前的汇总信息
                if (SummaryCache.ClientId != null && SummaryCache.ClientId != clientId && SummaryCache.AudioChunkCount > 0)
                {
                    WriteLog("DEBUG", $"[{SummaryCache.ClientId}] 累积WAV数据汇总: {SummaryCache.AudioChunkCount}个数据块, 总计:{SummaryCache.AudioTotalBytes}字节");
                    SummaryCache.AudioChunkCount = 0;
                    SummaryCache.AudioTotalBytes = 0;
                }

                SummaryCache.ClientId = clientId;
                SummaryCache.AudioChunkCount++;
                SummaryCache.AudioTotalBytes = totalSize;

                // 达到汇总频率，输出汇总信息
                if (SummaryCache.AudioChunkCount >= _summaryFrequency)
                {
                    WriteLog("DEBUG", $"[{clientId}] 累积WAV数据汇总: {SummaryCache.AudioChunkCount}个数据块, 总计:{totalSize}字节");
                    SummaryCache.AudioChunkCount = 0;
                }
            }
        }

        /// <summary>
        /// 写入日志
        /// </summary>
        /// <param name="level">日志级别</param>
        /// <param name="message">日志消息</param>
        private static void WriteLog(string level, string message)
        {
            try
            {
                string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{Thread.CurrentThread.ManagedThreadId}] {message}";

                // 输出到控制台
                if (ConsoleOutput)
                {
                    // 根据级别设置不同颜色
                    ConsoleColor originalColor = Console.ForegroundColor;
                    switch (level)
                    {
                        case "DEBUG":
                            Console.ForegroundColor = ConsoleColor.Gray;
                            break;
                        case "INFO":
                            Console.ForegroundColor = ConsoleColor.White;
                            break;
                        case "WARN":
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            break;
                        case "ERROR":
                            Console.ForegroundColor = ConsoleColor.Red;
                            break;
                    }

                    Console.WriteLine(logMessage);
                    Console.ForegroundColor = originalColor;
                }

                // 写入文件
                lock (_lock)
                {
                    if (_logFileWriter != null)
                    {
                        _logFileWriter.WriteLine(logMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                // 日志系统出错时，直接输出到控制台
                Console.WriteLine($"日志系统出错: {ex.Message}");
            }
        }
    }
}