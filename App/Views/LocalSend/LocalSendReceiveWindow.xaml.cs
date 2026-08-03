using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SwiftList.App.Helpers.Visuals;
using SwiftList.App.Services;
using SwiftList.App.Services.Theme;
using SwiftList.Core.Services.LocalSend;
using SwiftList.Core.Services.LocalSend.Models;
using System.Windows.Data;
using System.Windows.Threading;

namespace SwiftList.App.Views.LocalSend;

public partial class LocalSendReceiveWindow : Window
{
    private readonly LocalSendUploadRequestArgs _requestArgs;
    private readonly Stopwatch _stopwatch = new();
    private long _lastBytes;
    private bool _isCompleted;
    private string? _currentSessionId;
    private string? _lastSavedPath;
    private string? _lastRootSavedPath;
    private List<LocalSendReceiveFileItem> _fileItems = new();
    private string _senderAlias = string.Empty;

    public LocalSendReceiveWindow(LocalSendUploadRequestArgs requestArgs)
    {
        InitializeComponent();
        _requestArgs = requestArgs;
        _currentSessionId = requestArgs.SessionId;
        SystemMenuBlocker.Attach(this);
        AltTabExcluder.Attach(this);
        ThemedWindowIconHelper.Apply(this);
        ThemedWindowIconHelper.Apply(TitleBarLogo, this);
        PopulateRequestData(requestArgs.Dto);
    }

    private void PopulateRequestData(PrepareUploadRequestDto dto)
    {
        _senderAlias = dto.Info.Alias;
        var deviceLabel = TranslationManager.Instance["Settings_LocalSend_Device"];
        TxtSender.Text = $"{deviceLabel}: {_senderAlias}";

        _fileItems = dto.Files.Select(kv =>
        {
            var pText = kv.Value.Preview?.Trim();
            var isTextItem = string.Equals(kv.Value.FileType, "text", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(pText) || kv.Value.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
            var isLink = Uri.TryCreate(pText, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
            var isFileLink = Uri.TryCreate(kv.Value.FileName, UriKind.Absolute, out var u2) && (u2.Scheme == Uri.UriSchemeHttp || u2.Scheme == Uri.UriSchemeHttps);
            var linkUrl = isLink ? pText : (isFileLink ? kv.Value.FileName : null);
            var isGuid = Guid.TryParse(System.IO.Path.GetFileNameWithoutExtension(kv.Value.FileName), out _);
            var displayName = !string.IsNullOrWhiteSpace(pText) ? pText : kv.Value.FileName;
            return new LocalSendReceiveFileItem { FileId = kv.Key, FileName = kv.Value.FileName, DisplayName = displayName, LinkUrl = linkUrl, TextContent = pText, IsTextItem = isTextItem, Size = kv.Value.Size, SizeText = LocalSendServerHelper.FormatBytes(kv.Value.Size) };
        }).ToList();

        var view = (ListCollectionView)CollectionViewSource.GetDefaultView(_fileItems);
        view.GroupDescriptions.Clear();
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LocalSendReceiveFileItem.GroupHeader)));

        LstFiles.ItemsSource = view;
        LstFiles.SelectAll();
        UpdateSummaryText();
    }

    private void HyperlinkCopy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LocalSendReceiveFileItem item }) { try { System.Windows.Clipboard.SetText(item.TextContent ?? item.DisplayName); } catch { } }
    }
    private void HyperlinkOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LocalSendReceiveFileItem item } && !string.IsNullOrEmpty(item.LinkUrl)) { try { Process.Start(new ProcessStartInfo(item.LinkUrl) { UseShellExecute = true }); } catch { } }
    }

    private void LstFiles_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateSummaryText();

    private void BtnToggleSelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (LstFiles.SelectedItems.Count == _fileItems.Count) LstFiles.UnselectAll();
        else LstFiles.SelectAll();
    }

    private void UpdateSummaryText()
    {
        var selectedFiles = LstFiles.SelectedItems.OfType<LocalSendReceiveFileItem>().Where(i => !i.IsTextItem).ToList();
        var totalBytes = selectedFiles.Sum(i => i.Size);
        var sizeFormatted = LocalSendServerHelper.FormatBytes(totalBytes);
        var msgFormat = TranslationManager.Instance["Settings_LocalSend_UploadRequestMsg"];
        TxtSummary.Text = string.Format(msgFormat, _senderAlias, selectedFiles.Count, sizeFormatted);

        var hasFiles = _fileItems.Any(i => !i.IsTextItem);
        var hasSelection = selectedFiles.Count > 0 || !hasFiles;
        BtnSaveTo.IsEnabled = hasSelection;
        BtnAcceptDefault.IsEnabled = hasSelection;
    }

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
        e.Handled = true;
    }
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (GridStep1Footer.Visibility == Visibility.Visible) BtnDecline_Click(this, new RoutedEventArgs());
            else if (!_isCompleted) BtnCloseProgress_Click(this, new RoutedEventArgs());
            else Close();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (GridStep1Footer.Visibility != Visibility.Visible && !_isCompleted)
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (!string.IsNullOrEmpty(_currentSessionId))
        {
            LocalSendServiceManager.Instance.UnregisterSession(_currentSessionId);
        }
    }

    private void BtnDecline_Click(object sender, RoutedEventArgs e)
    {
        _requestArgs.Respond(false);
        Close();
    }

    private bool ApplySelectedFiles()
    {
        var selected = LstFiles.SelectedItems.OfType<LocalSendReceiveFileItem>().Select(i => i.FileId).ToHashSet();
        if (selected.Count == 0)
        {
            BtnDecline_Click(this, new RoutedEventArgs());
            return false;
        }
        _requestArgs.SelectedFileIds = selected;
        return true;
    }

    private void BtnAcceptDefault_Click(object sender, RoutedEventArgs e)
    {
        if (_isCompleted || LocalSendServiceManager.Instance.IsSessionCanceled(_currentSessionId ?? string.Empty))
        {
            ShowSenderCanceledInStep1();
            return;
        }
        if (!ApplySelectedFiles()) return;
        _requestArgs.Respond(true);
        SwitchToProgressStep();
    }

    private void BtnSaveTo_Click(object sender, RoutedEventArgs e)
    {
        var title = TranslationManager.Instance["Settings_LocalSend_UploadRequestTitle"];
        var dialog = new OpenFolderDialog { Title = title };
        if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            if (_isCompleted || LocalSendServiceManager.Instance.IsSessionCanceled(_currentSessionId ?? string.Empty))
            {
                ShowSenderCanceledInStep1();
                return;
            }
            if (!ApplySelectedFiles()) return;
            _requestArgs.CustomDownloadDirectory = dialog.FolderName;
            _requestArgs.Respond(true);
            SwitchToProgressStep();
        }
    }

    private void ShowSenderCanceledInStep1()
    {
        _isCompleted = true; _requestArgs.Respond(false); LstFiles.UnselectAll(); LstFiles.IsHitTestVisible = false;
        BtnToggleSelectAll.Visibility = Visibility.Collapsed; GridStep1Footer.Visibility = Visibility.Collapsed; PanelStep2Footer.Visibility = Visibility.Visible;
        TxtSummary.Text = TranslationManager.Instance["Settings_LocalSend_SenderCanceled"]; BtnCloseProgress.Content = TranslationManager.Instance["Common_Close"];
    }

    private void SwitchToProgressStep()
    {
        var fileOnlyItems = LstFiles.SelectedItems.OfType<LocalSendReceiveFileItem>().Where(i => !i.IsTextItem).ToList();
        if (fileOnlyItems.Count == 0)
        {
            _isCompleted = true;
            Close();
            return;
        }
        LstFiles.ItemsSource = fileOnlyItems;
        LstFiles.UnselectAll();
        LstFiles.IsHitTestVisible = false;

        BtnToggleSelectAll.Visibility = Visibility.Collapsed;
        GridStep1Footer.Visibility = Visibility.Collapsed;
        PanelStep2Footer.Visibility = Visibility.Visible;
        TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_Receiving"];
        _stopwatch.Start();
        ResetInactivityTimer();
    }

    private DispatcherTimer? _inactivityTimer;
    private void ResetInactivityTimer()
    {
        _inactivityTimer?.Stop();
        if (_isCompleted) return;
        _inactivityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _inactivityTimer.Tick += (_, _) => { _inactivityTimer?.Stop(); if (!_isCompleted) HandleSessionCanceled(_currentSessionId ?? string.Empty); };
        _inactivityTimer.Start();
    }

    private int _maxCompletedCount;

    public void HandleProgressChanged(LocalSendProgressArgs args) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (_isCompleted && !args.IsAllDone) return;

        ResetInactivityTimer();
        _currentSessionId = args.SessionId;
        var isAllDone = args.IsAllDone;
        if (isAllDone) { _isCompleted = true; _inactivityTimer?.Stop(); }

        UpdateFileItemsProgress(args);

        var realFinishedCount = isAllDone ? args.TotalFiles : LstFiles.Items.OfType<LocalSendReceiveFileItem>().Count(i => i.IsFinished);
        _maxCompletedCount = Math.Max(_maxCompletedCount, realFinishedCount);

        TxtSummary.Text = $"({_maxCompletedCount}/{args.TotalFiles})";
        TxtWindowTitle.Text = $"{TranslationManager.Instance["Settings_LocalSend_Receiving"]} ({_maxCompletedCount}/{args.TotalFiles})";

        var elapsedSec = _stopwatch.Elapsed.TotalSeconds;
        var curBytes = args.SessionBytesTransferred > 0 ? args.SessionBytesTransferred : args.BytesTransferred;
        if (elapsedSec >= 0.3 || _lastBytes == 0)
        {
            var speed = elapsedSec > 0 && curBytes >= _lastBytes ? (curBytes - _lastBytes) / elapsedSec : 0;
            TxtSpeed.Text = $"{LocalSendServerHelper.FormatBytes((long)Math.Max(0, speed))}/s";
            _lastBytes = curBytes;
            _stopwatch.Restart();
        }

        if (isAllDone)
        {
            _inactivityTimer?.Stop();
            _lastSavedPath = args.SavedPath;
            _lastRootSavedPath = args.RootSavedPath;
            BtnCloseProgress.Content = TranslationManager.Instance["Common_Close"];
            var target = LocalSendReceiveWindowHelper.ResolveFolderTarget(_lastRootSavedPath, _lastSavedPath);
            if (!string.IsNullOrEmpty(target)) BtnOpenFolder.Visibility = Visibility.Visible;
        }
    }));

    private void UpdateFileItemsProgress(LocalSendProgressArgs args) => PbTransfer.Value = LocalSendReceiveWindowHelper.UpdateItemProgress(LstFiles.Items, args);

    public void HandleSessionCanceled(string sessionId) => Dispatcher.BeginInvoke(new Action(() =>
    {
        _isCompleted = true; _inactivityTimer?.Stop();
        if (GridStep1Footer.Visibility == Visibility.Visible) ShowSenderCanceledInStep1();
        else { TxtSpeed.Text = TranslationManager.Instance["Settings_LocalSend_SenderCanceled"]; BtnCloseProgress.Content = TranslationManager.Instance["Common_Close"]; }
    }));

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var target = LocalSendReceiveWindowHelper.ResolveFolderTarget(_lastRootSavedPath, _lastSavedPath);
        if (!string.IsNullOrEmpty(target)) { try { Process.Start("explorer.exe", $"/select,\"{target}\""); } catch { } }
        Close();
    }
    private void BtnCloseProgress_Click(object sender, RoutedEventArgs e)
    {
        if (!_isCompleted && !string.IsNullOrEmpty(_currentSessionId))
        {
            _isCompleted = true;
            TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_Canceled"];
            TxtSpeed.Text = TranslationManager.Instance["Settings_LocalSend_Canceled"];
            BtnCloseProgress.Content = TranslationManager.Instance["Common_Close"];
            LocalSendServiceManager.Instance.CancelSession(_currentSessionId);
            return;
        }
        Close();
    }
}
