using System;
using System.IO;
using System.Text.Json;
using System.Text;

namespace StockAnalysisSystem.Core.Utils
{
    /// <summary>
    /// 错误日志记录器
    /// </summary>
    public static class ErrorLogger
    {
        private static readonly string LogDir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "logs");
            
        private static readonly string LogPath = Path.Combine(
            LogDir, $"error_{DateTime.Now:yyyyMMdd}.log");

        /// <summary>
        /// 记录异常到日志文件
        /// </summary>
        /// <param name="ex">异常对象</param>
        /// <param name="context">上下文信息（方法名等）</param>
        /// <param name="parameters">方法参数（将序列化为JSON）</param>
        public static void Log(Exception ex, string? context = null, object? parameters = null)
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                
                var logContent = new StringBuilder()
                    .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}]")
                    .AppendLine($"Context: {context ?? "N/A"}")
                    .AppendLine($"Exception Type: {ex.GetType().FullName}")
                    .AppendLine($"Message: {ex.Message}")
                    .AppendLine($"Source: {ex.Source ?? "N/A"}")
                    .AppendLine($"StackTrace:\n{ex.StackTrace}");
                
                if (parameters != null)
                {
                    try
                    {
                        logContent.AppendLine($"Parameters:\n{JsonSerializer.Serialize(parameters, new JsonSerializerOptions { WriteIndented = true })}");
                    }
                    catch { /* 忽略序列化错误 */ }
                }
                
                logContent.AppendLine(new string('-', 60));
                
                File.AppendAllText(LogPath, logContent.ToString());
            }
            catch
            {
                // 确保日志记录本身不会导致程序崩溃
            }
        }

        /// <summary>
        /// 获取当前日志文件路径
        /// </summary>
        public static string CurrentLogPath => LogPath;
    }
}
