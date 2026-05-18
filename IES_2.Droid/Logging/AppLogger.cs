namespace IES_2.Droid
{
    /// <summary>
    /// Lightweight logger: writes to Android logcat (tag "IES2") and appends to
    /// a rolling text file in the app cache directory for easy post-mortem inspection.
    /// </summary>
    internal static class AppLogger
    {
        private const string Tag = "IES2";
        private const long MaxFileBytes = 512 * 1024; // 512 KB rolling limit

        private static readonly string _logFilePath;

        static AppLogger()
        {
            try
            {
                string dir = FileSystem.CacheDirectory;
                _logFilePath = Path.Combine(dir, "ies2_debug.log");
                // Roll log file when it gets too large
                if (File.Exists(_logFilePath) && new FileInfo(_logFilePath).Length > MaxFileBytes)
                    File.Delete(_logFilePath);
            }
            catch { }
        }

        /// <summary>Full path to the log file (share or display for diagnostics).</summary>
        public static string LogFilePath => _logFilePath;

        public static void Log(string message)
        {
            Android.Util.Log.Debug(Tag, message);
            AppendLine("DBG", message);
        }

        public static void LogWarning(string message)
        {
            Android.Util.Log.Warn(Tag, message);
            AppendLine("WRN", message);
        }

        public static void LogError(string message, Exception ex = null)
        {
            string full = ex == null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
            Android.Util.Log.Error(Tag, full);
            AppendLine("ERR", full);
        }

        private static void AppendLine(string level, string text)
        {
            if (_logFilePath == null) return;
            try
            {
                File.AppendAllText(_logFilePath, $"{DateTime.Now:HH:mm:ss.fff} [{level}] {text}\n");
            }
            catch { /* never throw from logger */ }
        }
    }
}
