using System;
using System.Collections.Generic;
using System.IO;

namespace TailscaleZHTray;

internal static class FileDropHelper
{
    /// <summary>一次最多发多少个文件，太多就拒绝，免得误拖整个盘。</summary>
    public const int MaxFiles = 500;

    /// <summary>把拖进来的路径展开成文件列表；文件夹递归进去。</summary>
    public static List<string> Expand(IEnumerable<string> paths)
    {
        var files = new List<string>();
        if (paths == null)
            return files;

        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                    files.AddRange(Directory.GetFiles(path, "*", SearchOption.AllDirectories));
                else if (File.Exists(path))
                    files.Add(path);
            }
            catch (Exception ex)
            {
                Logger.Log($"展开拖放路径失败: {path} => {ex.Message}");
            }
        }

        return files;
    }

    /// <summary>确认框里展示的预览文本（最多列 8 个）。</summary>
    public static string Preview(IReadOnlyList<string> files)
    {
        if (files.Count == 0)
            return "";

        var text = "";
        var shown = Math.Min(files.Count, 8);
        for (var i = 0; i < shown; i++)
            text += (i == 0 ? "" : Environment.NewLine) + Path.GetFileName(files[i]);

        if (files.Count > shown)
            text += $"{Environment.NewLine}…以及另外 {files.Count - shown} 个文件";

        return text;
    }
}
