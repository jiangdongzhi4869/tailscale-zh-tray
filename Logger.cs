using System;
using System.IO;

namespace TailscaleZHTray;

internal static class Logger
{
    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TailscaleZHTray");

    public static readonly string FilePath = Path.Combine(Dir, "tray.log");

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
        }
        catch
        {
            // 记日志失败不影响主功能
        }
    }
}
