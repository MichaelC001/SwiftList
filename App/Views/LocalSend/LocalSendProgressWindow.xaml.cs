using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using SwiftList.App.Helpers.Visuals;
using SwiftList.App.Services;
using SwiftList.App.Services.Theme;
using SwiftList.Core.Services.LocalSend;

namespace SwiftList.App.Views.LocalSend;

public partial class LocalSendProgressWindow : Window
{
    private string? _lastSavedPath;
    private long _lastBytes;
    private Stopwatch _stopwatch = Stopwatch.StartNew();
    private string? _currentSessionId;
    private bool _isCompleted;
    private int _lastFileIndex = -1;
    private string? _lastRootSavedPath;

    private string? _titleKey;
    private string? _speedKey;
    private string? _btnCloseKey;
    private string? _lastSenderAlias;

    public LocalSendProgressWindow()
    {
        InitializeComponent();

        SystemMenuBlocker.Attach(this);
        AltTabExcluder.Attach(this);
        ThemedWindowIconHelper.Apply(this);
        ThemedWindowIconHelper.Apply(TitleBarLogo, this);

        TranslationManager.Instance.PropertyChanged += OnLanguageChanged;
        Closed += (_, _) => TranslationManager.Instance.PropertyChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RefreshLocalizedTexts();

    private void RefreshLocalizedTexts()
    {
        if (!string.IsNullOrEmpty(_lastSenderAlias))
        {
            var deviceLabel = TranslationManager.Instance["Settings_LocalSend_Device"];
            TxtSender.Text = $"{deviceLabel}: {_lastSenderAlias}";
        }
        if (!string.IsNullOrEmpty(_titleKey))
        {
            TxtTitle.Text = TranslationManager.Instance[_titleKey];
        }
        if (!string.IsNullOrEmpty(_speedKey))
        {
            TxtSpeed.Text = TranslationManager.Instance[_speedKey];
        }
        if (!string.IsNullOrEmpty(_btnCloseKey))
        {
            BtnClose.Content = TranslationManager.Instance[_btnCloseKey];
        }
    }

    public void UpdateProgress(LocalSendProgressArgs args)
    {
        _currentSessionId = args.SessionId;
        _lastSenderAlias = args.SenderAlias;

        if (_lastFileIndex != args.CurrentFileIndex)
        {
            _lastFileIndex = args.CurrentFileIndex;
            _lastBytes = 0;
            _stopwatch.Restart();
        }

        var isAllDone = args.IsAllDone;
        if (isAllDone) _isCompleted = true;

        var displayIdx = isAllDone ? args.TotalFiles : args.CurrentFileIndex;
        var deviceLabel = TranslationManager.Instance["Settings_LocalSend_Device"];
        TxtSender.Text = $"{deviceLabel}: {args.SenderAlias}";
        TxtFileCount.Text = $"{displayIdx}/{args.TotalFiles}";
        TxtFileName.Text = args.FileName;

        if (args.SessionTotalBytes > 0)
        {
            var percent = (double)args.SessionBytesTransferred / args.SessionTotalBytes * 100;
            PbTransfer.Value = isAllDone ? 100 : Math.Min(100, Math.Max(0, percent));
            var displayTransferred = isAllDone ? args.SessionTotalBytes : args.SessionBytesTransferred;
            TxtSize.Text = $"{FormatBytes(displayTransferred)} / {FormatBytes(args.SessionTotalBytes)}";
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
            _titleKey = "Settings_LocalSend_FileReceivedTitle";
            _speedKey = "Settings_LocalSend_Completed";
            _btnCloseKey = "Common_Close";
            TxtTitle.Text = TranslationManager.Instance[_titleKey];
            TxtSpeed.Text = TranslationManager.Instance[_speedKey];
            PbTransfer.Value = 100;
            BtnOpenFolder.Visibility = Visibility.Visible;
            BtnClose.Content = TranslationManager.Instance[_btnCloseKey];
        }
        else
        {
            _titleKey = "Settings_LocalSend_Receiving";
            _btnCloseKey = "Common_Cancel";
            TxtTitle.Text = TranslationManager.Instance[_titleKey];
            BtnOpenFolder.Visibility = Visibility.Collapsed;
            BtnClose.Content = TranslationManager.Instance[_btnCloseKey];

            var elapsedSec = _stopwatch.Elapsed.TotalSeconds;
            if (elapsedSec >= 0.3 || _lastBytes == 0)
            {
                _speedKey = null;
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
            _titleKey = "Settings_LocalSend_Canceled";
            _speedKey = "Settings_LocalSend_SenderCanceled";
            _btnCloseKey = "Common_Close";
            TxtTitle.Text = TranslationManager.Instance[_titleKey];
            TxtSpeed.Text = TranslationManager.Instance[_speedKey];
            BtnOpenFolder.Visibility = Visibility.Collapsed;
            BtnClose.Content = TranslationManager.Instance[_btnCloseKey];
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

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close();
        }
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
            _titleKey = "Settings_LocalSend_Canceled";
            _speedKey = "Settings_LocalSend_Canceled";
            TxtTitle.Text = TranslationManager.Instance[_titleKey];
            TxtSpeed.Text = TranslationManager.Instance[_speedKey];
            LocalSendServiceManager.Instance.CancelSession(_currentSessionId);
        }
        Close();
    }
}
