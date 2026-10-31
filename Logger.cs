using System;
using System.IO;

namespace TailscaleZHTray;

internal static class Logger
{
    // 便携化：日志与运行数据统一放在 exe 同级的 data 子目录，
    // 不写系统用户目录，换机器时整个文件夹拷走即可。
    public static readonly string Dir = Path.Combine(
        AppContext.BaseDirectory,
        "data");

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
