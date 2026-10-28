using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TailscaleZHTray;

/// <summary>取系统「下载」文件夹的真实路径（用户可能把它挪到别的盘）。</summary>
internal static class KnownFolders
{
    // {374DE290-123F-4565-9164-39C4925E467B} = FOLDERID_Downloads
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(
        ref Guid rfid, uint flags, IntPtr token, out IntPtr path);

    public static string DownloadsPath
    {
        get
        {
            IntPtr ptr = IntPtr.Zero;
            try
            {
                var id = DownloadsId;
                if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out ptr) == 0 && ptr != IntPtr.Zero)
                {
                    var path = Marshal.PtrToStringUni(ptr);
                    if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                        return path;
                }
            }
            catch
            {
                // 落到下面的兜底路径
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(ptr);
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    /// <summary>用资源管理器打开下载目录，打不开就提示。</summary>
    public static void OpenDownloads(IWin32Window owner = null)
    {
        try
        {
            var path = DownloadsPath;
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "打开下载目录失败：" + ex.Message,
                "Tailscale 中文托盘", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
