using System;
using TaleWorlds.Library;

namespace PickItUp
{
    /// <summary>
    /// 调试工具类
    /// </summary>
    public static class DebugHelper
    {
        private static readonly bool IsDebugMode = 
#if DEBUG
            true;
#else
            false;
#endif

        private const string LOG_PREFIX = "[AH-PickItUp]";
        private static readonly object _lockObj = new object();

        /// <summary>
        /// 输出调试信息
        /// </summary>
        /// <param name="module">模块名称</param>
        /// <param name="message">调试信息</param>
        public static void Log(string module, string message)
        {
#if DEBUG
            lock (_lockObj)
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                string formattedMessage = $"{LOG_PREFIX}[{timestamp}][{module}] {message}";
                System.Diagnostics.Debug.WriteLine(formattedMessage);
                
                // 如果需要，也可以输出到游戏内的消息系统
                if (module.Contains("错误") || module.Contains("Error"))
                {
                    InformationManager.DisplayMessage(new InformationMessage(formattedMessage, Colors.Red));
                }
            }
#endif
        }

        /// <summary>
        /// 输出警告信息
        /// </summary>
        /// <param name="module">模块名称</param>
        /// <param name="message">警告信息</param>
        public static void LogWarning(string module, string message)
        {
#if DEBUG
            lock (_lockObj)
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                string formattedMessage = $"{LOG_PREFIX}[{timestamp}][{module}][警告] {message}";
                System.Diagnostics.Debug.WriteLine(formattedMessage);
                InformationManager.DisplayMessage(new InformationMessage(formattedMessage, Colors.Yellow));
            }
#endif
        }

        /// <summary>
        /// 输出错误信息
        /// </summary>
        /// <param name="module">模块名称</param>
        /// <param name="message">错误信息</param>
        /// <param name="ex">异常对象（可选）</param>
        public static void LogError(string module, string message, Exception ex = null)
        {
#if DEBUG
            lock (_lockObj)
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                string formattedMessage = $"{LOG_PREFIX}[{timestamp}][{module}][错误] {message}";
                if (ex != null)
                {
                    formattedMessage += $"\n{LOG_PREFIX}[{timestamp}][{module}][错误详情] {ex.GetType().Name}: {ex.Message}";
                    if (!string.IsNullOrEmpty(ex.StackTrace))
                    {
                        formattedMessage += $"\n{LOG_PREFIX}[{timestamp}][{module}][堆栈跟踪]\n{ex.StackTrace}";
                    }
                }
                System.Diagnostics.Debug.WriteLine(formattedMessage);
                InformationManager.DisplayMessage(new InformationMessage(formattedMessage, Colors.Red));
            }
#endif
        }
        /// <summary>
        /// 检查是否处于调试模式
        /// </summary>
        public static bool IsDebug => IsDebugMode;
    }
} 