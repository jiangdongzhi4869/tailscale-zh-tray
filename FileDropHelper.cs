using System;
using System.IO;

namespace TailscaleZHTray;

/// <summary>拖放只接受单个文件：多选和文件夹一律拒绝，并给出原因。</summary>
internal static class FileDropHelper
{
    /// <summary>校验拖进来的内容；通过时 file 是那个文件的完整路径。</summary>
    public static bool TryGetSingleFile(string[] dropped, out string file, out string error)
    {
        file = null;
        error = null;

        if (dropped == null || dropped.Length == 0)
        {
            error = "没有检测到文件。";
            return false;
        }

        if (dropped.Length > 1)
        {
            error = $"一次只能发送一个文件，这次拖了 {dropped.Length} 个。";
            return false;
        }

        var path = dropped[0];

        if (Directory.Exists(path))
        {
            error = "不支持发送文件夹。请先压缩成一个文件，或者一次只拖一个文件。";
            return false;
        }

        if (!File.Exists(path))
        {
            error = "这个路径读不到文件。";
            return false;
        }

        file = path;
        return true;
    }

    /// <summary>拖到半路时判断能不能接收：只放行"恰好一个文件"。</summary>
    public static bool IsSingleFile(IDataObject data)
    {
        if (data == null || !data.GetDataPresent(DataFormats.FileDrop))
            return false;

        if (data.GetData(DataFormats.FileDrop) is not string[] dropped || dropped.Length != 1)
            return false;

        return !Directory.Exists(dropped[0]);
    }
}
