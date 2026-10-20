using NLog;
using NLog.Config;
using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace YolanLive.WinUI.Helper
{
    public enum LogType
    {
        INFO,
        DEBUG,
        ERROR,
        FATAL
    }
    public class LogHelper
    {
        private static readonly object ConfigGate = new();
        public static LoggingConfiguration config;
        public static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        public static void Log(string message, LogType type, Exception ex = null)
        {
            lock (ConfigGate)
            {
                if (config == null)
                {
                    config = new NLog.Config.LoggingConfiguration();
                    var storagePath = AppPaths.EnsureDirectory();
                    var logDirectory = Path.Combine(storagePath, "log");
                    Directory.CreateDirectory(logDirectory);
                    // Retire only dated log files produced by older YolanLive versions.
                    foreach (var path in Directory.EnumerateFiles(logDirectory, "????????.log"))
                    {
                        if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path), "yyyyMMdd", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var date) && date < DateTime.Today.AddDays(-7))
                        {
                            try { File.Delete(path); }
                            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                            { Debug.WriteLine("旧日志清理失败：" + error.Message); }
                        }
                    }
                    var logfile = new NLog.Targets.FileTarget()
                    {
                        Name = "logfile",
                        CreateDirs = true,
                        FileName = Path.Combine(logDirectory, "YolanLive.log"),
                        ArchiveFileName = Path.Combine(logDirectory, "YolanLive.{#}.log"),
                        ArchiveNumbering = NLog.Targets.ArchiveNumberingMode.DateAndSequence,
                        ArchiveDateFormat = "yyyyMMdd",
                        ArchiveEvery = NLog.Targets.FileArchivePeriod.Day,
                        ArchiveAboveSize = 10 * 1024 * 1024,
                        MaxArchiveFiles = 7,
                        MaxArchiveDays = 7,
                        Layout = "${longdate}|${level:uppercase=true}|${logger}|${threadid}|${message}|${exception:format=Message,StackTrace}"
                    };
                    config.AddRule(LogLevel.Info, LogLevel.Info, logfile);
                    config.AddRule(LogLevel.Debug, LogLevel.Debug, logfile);
                    config.AddRule(LogLevel.Error, LogLevel.Error, logfile);
                    config.AddRule(LogLevel.Fatal, LogLevel.Fatal, logfile);
                    NLog.LogManager.Configuration = config;
                }
            }
            Debug.WriteLine("[" + LogType.INFO.ToString() + "]" + message);
            switch (type)
            {
                case LogType.INFO:
                    logger.Info(message);
                    break;
                case LogType.DEBUG:
                    logger.Debug(message);
                    break;
                case LogType.ERROR:
                    logger.Error(ex, message);
                    break;
                case LogType.FATAL:
                    logger.Fatal(ex, message);
                    break;
                default:
                    break;
            }
        }
        public static bool IsNetworkError(Exception ex)
        {
            if (ex.HResult == -2147012867 || ex.HResult == -2147012889)
            {
                return true;
            }
            {
                return false;
            }
        }

    }
}
