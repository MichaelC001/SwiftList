using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SwiftList.App.Helpers.Visuals;
using SwiftList.App.Services;
using SwiftList.App.Services.Theme;
using SwiftList.Core.Services.LocalSend;
using SwiftList.Core.Services.LocalSend.Models;

namespace SwiftList.App.Views.LocalSend;

public sealed class LocalSendReceiveFileItem
{
    public required string FileName { get; init; }
    public required string SizeText { get; init; }
}

public partial class LocalSendReceiveWindow : Window
{
    private readonly LocalSendUploadRequestArgs _requestArgs;
    private readonly Stopwatch _stopwatch = new();
    private long _lastBytes;
    private bool _isCompleted;
    private string? _currentSessionId;
    private string? _lastSavedPath;
    private string? _lastRootSavedPath;

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
        var deviceLabel = TranslationManager.Instance["Settings_LocalSend_Device"];
        TxtSender.Text = $"{deviceLabel}: {dto.Info.Alias}";

        var totalBytes = dto.Files.Values.Sum(f => f.Size);
        var sizeFormatted = LocalSendServerHelper.FormatBytes(totalBytes);
        var msgFormat = TranslationManager.Instance["Settings_LocalSend_UploadRequestMsg"];
        TxtSummary.Text = string.Format(msgFormat, dto.Info.Alias, dto.Files.Count, sizeFormatted);

        var items = dto.Files.Values.Select(f => new LocalSendReceiveFileItem
        {
            FileName = f.FileName,
            SizeText = LocalSendServerHelper.FormatBytes(f.Size)
        }).ToList();

        LstFiles.ItemsSource = items;
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
            if (GridRequestStep.Visibility == Visibility.Visible)
            {
                BtnDecline_Click(this, new RoutedEventArgs());
            }
            else
            {
                BtnCloseProgress_Click(this, new RoutedEventArgs());
            }
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (GridProgressStep.Visibility == Visibility.Visible && !_isCompleted)
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

    private void BtnAcceptDefault_Click(object sender, RoutedEventArgs e)
    {
        if (_isCompleted || LocalSendServiceManager.Instance.IsSessionCanceled(_currentSessionId ?? string.Empty))
        {
            ShowSenderCanceledInStep1();
            return;
        }
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
            _requestArgs.CustomDownloadDirectory = dialog.FolderName;
            _requestArgs.Respond(true);
            SwitchToProgressStep();
        }
    }

    private void ShowSenderCanceledInStep1()
    {
        _isCompleted = true;
        _requestArgs.Respond(false);
        TxtSummary.Text = TranslationManager.Instance["Settings_LocalSend_SenderCanceled"];
        BtnAcceptDefault.Visibility = Visibility.Collapsed;
        BtnSaveTo.Visibility = Visibility.Collapsed;
        BtnDecline.Content = TranslationManager.Instance["Common_Close"];
    }

    private void SwitchToProgressStep()
    {
        GridRequestStep.Visibility = Visibility.Collapsed;
        GridProgressStep.Visibility = Visibility.Visible;
        TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_Receiving"];
        _stopwatch.Start();
    }

    public void HandleProgressChanged(LocalSendProgressArgs args) => Dispatcher.BeginInvoke(new Action(() =>
    {
        _currentSessionId = args.SessionId;
        var isAllDone = args.IsAllDone;
        _isCompleted = isAllDone;

        TxtFileName.Text = args.FileName;
        TxtFileName.ToolTip = args.FileName;

        var completedCount = isAllDone ? args.TotalFiles : Math.Max(0, args.CurrentFileIndex - 1);
        TxtCounter.Text = $"({completedCount}/{args.TotalFiles})";

        if (args.TotalBytes > 0)
        {
            var percentage = (double)args.BytesTransferred / args.TotalBytes * 100.0;
            PbTransfer.Value = Math.Clamp(percentage, 0, 100);
        }

        if (!string.IsNullOrEmpty(args.SavedPath))
            _lastSavedPath = args.SavedPath;
        if (!string.IsNullOrEmpty(args.RootSavedPath))
            _lastRootSavedPath = args.RootSavedPath;

        if (isAllDone)
        {
            TxtProgressStatus.Text = TranslationManager.Instance["Settings_LocalSend_FileReceivedTitle"];
            TxtSpeed.Text = TranslationManager.Instance["Settings_LocalSend_Completed"];
            BtnCloseProgress.Content = TranslationManager.Instance["Common_Close"];
            PbTransfer.Value = 100;
            BtnOpenFolder.Visibility = Visibility.Visible;
        }
        else
        {
            TxtProgressStatus.Text = TranslationManager.Instance["Settings_LocalSend_Receiving"];
            BtnCloseProgress.Content = TranslationManager.Instance["Common_Cancel"];
            BtnOpenFolder.Visibility = Visibility.Collapsed;

            var elapsedSec = _stopwatch.Elapsed.TotalSeconds;
            if (elapsedSec >= 0.3 || _lastBytes == 0)
            {
                var bytesDelta = args.BytesTransferred - _lastBytes;
                var speedBytesPerSec = elapsedSec > 0 ? bytesDelta / elapsedSec : 0;
                TxtSpeed.Text = $"{LocalSendServerHelper.FormatBytes((long)Math.Max(0, speedBytesPerSec))}/s";

                _lastBytes = args.BytesTransferred;
                _stopwatch.Restart();
            }
        }
    }));

    public void HandleSessionCanceled(string sessionId) => Dispatcher.BeginInvoke(new Action(() =>
                                                                {
                                                                    _isCompleted = true;
                                                                    if (GridRequestStep.Visibility == Visibility.Visible)
                                                                    {
                                                                        ShowSenderCanceledInStep1();
                                                                    }
                                                                    else
                                                                    {
                                                                        TxtProgressStatus.Text = TranslationManager.Instance["Settings_LocalSend_Canceled"];
                                                                        TxtSpeed.Text = TranslationManager.Instance["Settings_LocalSend_SenderCanceled"];
                                                                        BtnCloseProgress.Content = TranslationManager.Instance["Common_Close"];
                                                                        BtnOpenFolder.Visibility = Visibility.Collapsed;
                                                                    }
                                                                }));

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

    private void BtnCloseProgress_Click(object sender, RoutedEventArgs e)
    {
        if (!_isCompleted && !string.IsNullOrEmpty(_currentSessionId))
        {
            _isCompleted = true;
            TxtProgressStatus.Text = TranslationManager.Instance["Settings_LocalSend_Canceled"];
            TxtSpeed.Text = TranslationManager.Instance["Settings_LocalSend_Canceled"];
            BtnCloseProgress.Content = TranslationManager.Instance["Common_Close"];
            LocalSendServiceManager.Instance.CancelSession(_currentSessionId);
            return;
        }
        Close();
    }
}
