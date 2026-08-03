using System.IO;
using System.Windows.Controls;
using SwiftList.App.Services;
using SwiftList.Core.Services.LocalSend;

namespace SwiftList.App.Views.LocalSend;

/// <summary>
/// UI helper methods for LocalSendReceiveWindow.xaml.cs.
/// ponytail: Split out purely to keep LocalSendReceiveWindow.xaml.cs under the repo's 300-line limit.
/// </summary>
public static class LocalSendReceiveWindowHelper
{
    public static string FormatSummaryFileName(string firstFileName, int totalFiles)
    {
        if (string.IsNullOrEmpty(firstFileName)) return string.Empty;
        return totalFiles > 1 ? $"{firstFileName} ({totalFiles})" : firstFileName;
    }

    public static string ResolveFolderTarget(string? rootPath, string? savedPath)
    {
        var target = rootPath ?? savedPath;
        if (!string.IsNullOrEmpty(target) && (File.Exists(target) || Directory.Exists(target)))
        {
            return target;
        }
        return string.Empty;
    }

    public static double UpdateItemProgress(ItemCollection items, LocalSendProgressArgs args)
    {
        var totalSessionBytes = args.SessionTotalBytes > 0 ? args.SessionTotalBytes : args.TotalBytes;
        var transferredSessionBytes = args.SessionBytesTransferred > 0 ? args.SessionBytesTransferred : args.BytesTransferred;
        var sessionPercentage = totalSessionBytes > 0 ? Math.Clamp((double)transferredSessionBytes / totalSessionBytes * 100.0, 0, 100) : 0;

        foreach (LocalSendReceiveFileItem item in items)
        {
            item.ShowProgress = true;
            if (args.IsAllDone || item.IsFinished)
            {
                item.IsFinished = true;
                item.ProgressPercentage = 100.0;
                item.StatusText = TranslationManager.Instance["Settings_LocalSend_Completed"];
            }
            else if (item.FileId == args.FileId)
            {
                var pct = args.TotalBytes > 0 ? (double)args.BytesTransferred / args.TotalBytes * 100.0 : 0;
                item.ProgressPercentage = Math.Min(100.0, pct);
                if (args.IsFinished)
                {
                    item.IsFinished = true;
                    item.ProgressPercentage = 100.0;
                    item.StatusText = TranslationManager.Instance["Settings_LocalSend_Completed"];
                }
                else
                {
                    item.StatusText = $"{item.ProgressPercentage:F0}%";
                }
            }
        }

        return sessionPercentage;
    }
}
