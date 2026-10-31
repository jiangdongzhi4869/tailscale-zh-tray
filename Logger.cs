using System;
using System.IO;

namespace TailscaleZHTray;

internal static class Logger
{
    private const long MaxBytes = 1_000_000;
    private static readonly object Gate = new();

    // 便携模式：日志与运行数据放在 exe 同级的 data 子目录；
    // 该目录不可写（例如程序装在 Program Files）时回退到用户目录。
    public static readonly string Dir = ResolveDir();

    public static readonly string FilePath = Path.Combine(Dir, "tray.log");

    private static string ResolveDir()
    {
        var portable = Path.Combine(AppContext.BaseDirectory, "data");
        try
        {
            Directory.CreateDirectory(portable);
            var probe = Path.Combine(portable, ".write-probe");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return portable;
        }
        catch
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TailscaleZHTray");
        }
    }

    public static void Log(string message)
    {
        try
        {
            // 多个后台任务会同时写日志，加锁避免 IOException 被吞掉
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                RotateIfNeeded();
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 记日志失败不影响主功能
        }
    }

    /// <summary>日志超过 1 MB 就转成 tray.log.1，只保留一份旧文件。</summary>
    private static void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(FilePath) || new FileInfo(FilePath).Length < MaxBytes)
                return;

            var backup = FilePath + ".1";
            if (File.Exists(backup))
                File.Delete(backup);
            File.Move(FilePath, backup);
        }
        catch
        {
            // 轮转失败就继续往原文件写
        }
    }
}
