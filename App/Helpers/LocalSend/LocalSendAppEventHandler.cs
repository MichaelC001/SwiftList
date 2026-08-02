using System.Diagnostics;
using System.Windows;
using SwiftList.App.Services;
using SwiftList.App.Views.LocalSend;
using SwiftList.Core;
using SwiftList.Core.Services.LocalSend;
using SwiftList.Core.Services.LocalSend.Models;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;
using MessageBox = SwiftList.App.Views.Controls.Dialogs.CustomMessageBox;

namespace SwiftList.App.Helpers.LocalSend;

/// <summary>
/// Handles LocalSend background events (progress, upload requests, text messages, cancellation).
/// ponytail: Split out purely to keep App.xaml.cs under the repo's 300-line limit.
/// </summary>
public static class LocalSendAppEventHandler
{
    private static LocalSendReceiveWindow? _activeReceiveWindow;
    private static LocalSendProgressArgs? _pendingProgressArgs;
    private static bool _isProgressDispatchPending;

    public static void Initialize(UserSettings settings)
    {
        var manager = LocalSendServiceManager.Instance;
        manager.ApplySettings(settings);
        manager.ProgressChanged += OnProgressChanged;
        manager.SessionCanceled += OnSessionCanceled;
        manager.TextReceived += OnTextReceived;
        manager.UploadRequested += OnUploadRequested;
        manager.SendRequested += OnSendRequested;
    }

    private static void OnProgressChanged(object? sender, LocalSendProgressArgs e)
    {
        _pendingProgressArgs = e;

        if (e.IsAllDone || e.IsFinished || !_isProgressDispatchPending)
        {
            _isProgressDispatchPending = true;
            Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                _isProgressDispatchPending = false;
                var argsToUpdate = _pendingProgressArgs;
                if (argsToUpdate == null) return;

                if (_activeReceiveWindow != null && _activeReceiveWindow.IsLoaded)
                {
                    _activeReceiveWindow.HandleProgressChanged(argsToUpdate);
                }
            }));
        }
    }

    private static void OnSessionCanceled(object? sender, string sessionId) => Application.Current.Dispatcher.BeginInvoke(new Action(() => _activeReceiveWindow?.HandleSessionCanceled(sessionId)));

    private static void OnTextReceived(object? sender, (string SenderAlias, string Text, bool IsLink) e) => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                                                                                                                 {
                                                                                                                     if (e.IsLink)
                                                                                                                     {
                                                                                                                         var title = TranslationManager.Instance["Settings_LocalSend_LinkReceivedTitle"];
                                                                                                                         var openText = TranslationManager.Instance["Settings_LocalSend_OpenInBrowser"];
                                                                                                                         var cancelText = TranslationManager.Instance["Common_Close"];
                                                                                                                         var msg = $"{e.SenderAlias}:\n{e.Text}";

                                                                                                                         var result = MessageBox.ShowCustom(msg, title, openText, cancelText, MessageBoxImage.Information);
                                                                                                                         if (result == MessageBoxResult.OK)
                                                                                                                         {
                                                                                                                             try { Process.Start(new ProcessStartInfo(e.Text) { UseShellExecute = true }); }
                                                                                                                             catch { }
                                                                                                                         }
                                                                                                                     }
                                                                                                                     else
                                                                                                                     {
                                                                                                                         var title = TranslationManager.Instance["Settings_LocalSend_TextReceivedTitle"];
                                                                                                                         var copyText = TranslationManager.Instance["Settings_LocalSend_CopyToClipboard"];
                                                                                                                         var cancelText = TranslationManager.Instance["Common_Close"];
                                                                                                                         var msg = $"{e.SenderAlias}:\n{e.Text}";

                                                                                                                         var result = MessageBox.ShowCustom(msg, title, copyText, cancelText, MessageBoxImage.Information);
                                                                                                                         if (result == MessageBoxResult.OK)
                                                                                                                         {
                                                                                                                             try { Clipboard.SetText(e.Text); }
                                                                                                                             catch (Exception ex) { Logger.Log($"[LocalSendAppEventHandler] Failed to set clipboard text: {ex.Message}", LogLevel.Warn); }
                                                                                                                         }
                                                                                                                     }
                                                                                                                 }));

    private static void OnSendRequested(object? sender, (IReadOnlyList<string>? Files, string? Text) e) => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                                                                                                                {
                                                                                                                    var sendWin = new LocalSendSendWindow(e.Files, e.Text);
                                                                                                                    sendWin.Show();
                                                                                                                    sendWin.Activate();
                                                                                                                }));

    private static void OnUploadRequested(object? sender, LocalSendUploadRequestArgs e) => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                                                                                                {
                                                                                                    _activeReceiveWindow = new LocalSendReceiveWindow(e);
                                                                                                    _activeReceiveWindow.Closed += (_, _) => _activeReceiveWindow = null;
                                                                                                    _activeReceiveWindow.ShowDialog();
                                                                                                }));
}
