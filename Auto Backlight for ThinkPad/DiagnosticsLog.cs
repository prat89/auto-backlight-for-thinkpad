using System;
using System.IO;
using System.Text;
namespace Auto_Backlight_for_ThinkPad
{
    internal static class DiagnosticsLog
    {
        private static readonly object Sync = new object();
        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Auto Backlight for ThinkPad", "Logs");
        public static string FilePath { get { return Path.Combine(Folder, "application.log"); } }
        public static void Info(string message) { Write("INFO", message); }
        public static void Error(string message, Exception error)
        {
            Write("ERROR", message + (error == null ? "" : Environment.NewLine + error.ToString()));
        }
        private static void Write(string level, string message)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(Folder);
                    if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 2 * 1024 * 1024)
                    {
                        string old = Path.Combine(Folder, "application.previous.log");
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(FilePath, old);
                    }
                    File.AppendAllText(FilePath, DateTimeOffset.Now.ToString("o") +
                        " [" + level + "] " + message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { /* Diagnostics must not affect runtime. */ }
        }
    }
}
