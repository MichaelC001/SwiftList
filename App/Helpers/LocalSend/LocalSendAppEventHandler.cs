using SwiftList.App.Views.LocalSend;
using SwiftList.Core;
using SwiftList.Core.Services.LocalSend;
using SwiftList.Core.Services.LocalSend.Models;
using Application = System.Windows.Application;

namespace SwiftList.App.Helpers.LocalSend;

/// <summary>
/// Handles LocalSend background events (progress, upload requests, text messages, cancellation).
/// ponytail: Split out purely to keep App.xaml.cs under the repo's 300-line limit.
/// </summary>
public static class LocalSendAppEventHandler
{
    private static LocalSendReceiveWindow? _activeReceiveWindow;
    private static LocalSendSendWindow? _activeSendWindow;
    private static LocalSendProgressArgs? _pendingProgressArgs;
    private static bool _isProgressDispatchPending;
    private static volatile bool _isReceiveWindowOpen;
    private static volatile bool _isSendWindowOpen;

    public static bool IsAnyWindowOpen => _isReceiveWindowOpen || _isSendWindowOpen;

    public static void Initialize(UserSettings settings)
    {
        var manager = LocalSendServiceManager.Instance;
        manager.WindowOpenCheck = () => IsAnyWindowOpen;
        manager.ApplySettings(settings);
        manager.ProgressChanged += OnProgressChanged;
        manager.SessionCanceled += OnSessionCanceled;
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

    private static void OnSessionCanceled(object? sender, string sessionId) => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
    {
        _activeReceiveWindow?.HandleSessionCanceled(sessionId);
        _activeSendWindow?.HandleSessionCanceled(sessionId);
    }));

    private static void OnSendRequested(object? sender, (IReadOnlyList<string>? Files, string? Text) e) => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
    {
        if (_activeSendWindow != null && _activeSendWindow.IsLoaded)
        {
            _activeSendWindow.Activate();
            return;
        }
        _activeSendWindow = new LocalSendSendWindow(e.Files, e.Text);
        _isSendWindowOpen = true;
        _activeSendWindow.Closed += (_, _) => { _activeSendWindow = null; _isSendWindowOpen = false; };
        _activeSendWindow.Show();
        _activeSendWindow.Activate();
    }));

    private static void OnUploadRequested(object? sender, LocalSendUploadRequestArgs e) => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
    {
        _activeReceiveWindow = new LocalSendReceiveWindow(e);
        _isReceiveWindowOpen = true;
        _activeReceiveWindow.Closed += (_, _) => { _activeReceiveWindow = null; _isReceiveWindowOpen = false; };
        _activeReceiveWindow.ShowDialog();
    }));
}
