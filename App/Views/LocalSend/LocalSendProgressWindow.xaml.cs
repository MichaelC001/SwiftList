using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using SwiftList.App.Helpers.Visuals;
using SwiftList.App.Services.Theme;
using SwiftList.Core.Services.LocalSend;

namespace SwiftList.App.Views.LocalSend;

public partial class LocalSendProgressWindow : Window
{
    private string? _lastSavedPath;
    private long _lastBytes;
    private Stopwatch _stopwatch = Stopwatch.StartNew();

    public LocalSendProgressWindow()
    {
        InitializeComponent();

        SystemMenuBlocker.Attach(this);
        ThemedWindowIconHelper.Apply(this);
        ThemedWindowIconHelper.Apply(TitleBarLogo, this);
    }

    private string? _currentSessionId;
    private bool _isCompleted;
    private int _lastFileIndex = -1;

    private string? _lastRootSavedPath;

    public void UpdateProgress(LocalSendProgressArgs args)
    {
        _currentSessionId = args.SessionId;

        if (_lastFileIndex != args.CurrentFileIndex)
        {
            _lastFileIndex = args.CurrentFileIndex;
            _lastBytes = 0;
            _stopwatch.Restart();
        }

        var isAllDone = args.IsAllDone;
        if (isAllDone) _isCompleted = true;

        var displayIdx = isAllDone ? args.TotalFiles : args.CurrentFileIndex;
        TxtSender.Text = $"设备: {args.SenderAlias}";
        TxtFileCount.Text = $"{displayIdx}/{args.TotalFiles}";
        TxtFileName.Text = args.FileName;

        if (args.SessionTotalBytes > 0)
        {
            var percent = (double)args.SessionBytesTransferred / args.SessionTotalBytes * 100;
            PbTransfer.Value = Math.Min(100, Math.Max(0, percent));
            TxtSize.Text = $"{FormatBytes(args.SessionBytesTransferred)} / {FormatBytes(args.SessionTotalBytes)}";
        }
        else
        {
            PbTransfer.Value = 100;
            TxtSize.Text = FormatBytes(args.SessionBytesTransferred);
        }

        if (!string.IsNullOrEmpty(args.SavedPath))
            _lastSavedPath = args.SavedPath;
        if (!string.IsNullOrEmpty(args.RootSavedPath))
            _lastRootSavedPath = args.RootSavedPath;

        if (isAllDone)
        {
            TxtTitle.Text = "文件接收完成";
            TxtSpeed.Text = "传输完成";
            PbTransfer.Value = 100;
            BtnOpenFolder.Visibility = Visibility.Visible;
            BtnClose.Content = "关闭";
        }
        else
        {
            TxtTitle.Text = "正在接收文件...";
            BtnOpenFolder.Visibility = Visibility.Collapsed;
            BtnClose.Content = "取消";

            var elapsedSec = _stopwatch.Elapsed.TotalSeconds;
            if (elapsedSec >= 0.5 || _lastBytes == 0)
            {
                var bytesDelta = args.BytesTransferred - _lastBytes;
                var speedBytesPerSec = elapsedSec > 0 ? bytesDelta / elapsedSec : 0;
                TxtSpeed.Text = $"{FormatBytes((long)Math.Max(0, speedBytesPerSec))}/s";

                _lastBytes = args.BytesTransferred;
                _stopwatch.Restart();
            }
        }
    }

    public void HandleSessionCanceled(string sessionId)
    {
        if (string.Equals(_currentSessionId, sessionId, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(_currentSessionId))
        {
            _isCompleted = true;
            TxtTitle.Text = "传输已取消";
            TxtSpeed.Text = "发送方已取消传输";
            BtnOpenFolder.Visibility = Visibility.Collapsed;
            BtnClose.Content = "关闭";
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{(double)bytes / 1024:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{(double)bytes / (1024 * 1024):F1} MB";
        return $"{(double)bytes / (1024 * 1024 * 1024):F2} GB";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var targetToSelect = _lastRootSavedPath ?? _lastSavedPath;
        if (!string.IsNullOrEmpty(targetToSelect) && (File.Exists(targetToSelect) || Directory.Exists(targetToSelect)))
        {
            try { Process.Start("explorer.exe", $"/select,\"{targetToSelect}\""); }
            catch { }
        }
        Close();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        if (!_isCompleted && !string.IsNullOrEmpty(_currentSessionId))
        {
            TxtTitle.Text = "传输已取消";
            TxtSpeed.Text = "已取消接收";
            LocalSendServiceManager.Instance.CancelSession(_currentSessionId);
        }
        Close();
    }
}
