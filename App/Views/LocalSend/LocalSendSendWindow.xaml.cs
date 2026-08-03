using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftList.App.Helpers.Visuals;
using SwiftList.App.Services;
using SwiftList.App.Services.Theme;
using SwiftList.App.ViewModels.LocalSend;

namespace SwiftList.App.Views.LocalSend;

public partial class LocalSendSendWindow : Window
{
    private readonly LocalSendSendViewModel _vm;

    public LocalSendSendWindow(IEnumerable<string>? initialFiles = null, string? initialText = null)
    {
        InitializeComponent();

        SystemMenuBlocker.Attach(this);
        ThemedWindowIconHelper.Apply(this);
        ThemedWindowIconHelper.Apply(TitleBarLogo, this);

        _vm = new LocalSendSendViewModel(initialFiles, initialText);
        DataContext = _vm;
        _vm.PropertyChanged += Vm_PropertyChanged;

        StateChanged += (_, _) => { if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal; };
        TranslationManager.Instance.PropertyChanged += OnLanguageChanged;
        Closed += (_, _) =>
        {
            TranslationManager.Instance.PropertyChanged -= OnLanguageChanged;
            _vm.Dispose();
        };

        UpdateStepVisibility();
    }

    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_vm.CurrentStep == 2)
        {
            if (_vm.StatusText.Contains(TranslationManager.Instance["Settings_LocalSend_Completed"]))
            {
                TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_Completed"];
            }
            else if (_cancelSource == CancelSource.Self)
            {
                TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_Canceled"];
            }
            else if (_cancelSource == CancelSource.Receiver || _vm.StatusText.Contains(TranslationManager.Instance["Settings_LocalSend_Declined"]))
            {
                TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_ReceiverCanceled"];
            }
            else if (_vm.StatusText.Contains(TranslationManager.Instance["Settings_LocalSend_Busy"]))
            {
                TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_Busy"];
            }
            else if (_vm.IsSending)
            {
                TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_Sending"];
            }
            else
            {
                TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_ConnectionError"];
            }

            BtnCancelOrClose.Content = _vm.IsSending ? TranslationManager.Instance["Common_Cancel"] : TranslationManager.Instance["Common_Close"];
        }
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_vm.CurrentStep))
        {
            UpdateStepVisibility();
        }
        else if (e.PropertyName == nameof(_vm.SelectedMode))
        {
            UpdateModeVisibility();
        }
    }

    private void UpdateStepVisibility()
    {
        GridStep0.Visibility = _vm.CurrentStep == 0 ? Visibility.Visible : Visibility.Collapsed;
        GridStep1.Visibility = _vm.CurrentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        GridStep2.Visibility = _vm.CurrentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        UpdateModeVisibility();
    }

    private void UpdateModeVisibility()
    {
        PanelFilesMode.Visibility = _vm.IsFilesMode ? Visibility.Visible : Visibility.Collapsed;
        PanelTextMode.Visibility = _vm.IsTextMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RadioModeFiles_Click(object sender, RoutedEventArgs e) => _vm.SelectedMode = 0;
    private void RadioModeText_Click(object sender, RoutedEventArgs e) => _vm.SelectedMode = 1;

    private void BtnAddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Title = "选择要发送的文件"
        };
        if (dlg.ShowDialog() == true)
        {
            _vm.AddPaths(dlg.FileNames);
        }
    }

    private void BtnAddFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "选择要发送的文件夹",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrEmpty(dlg.SelectedPath))
        {
            _vm.AddPaths(new[] { dlg.SelectedPath });
        }
    }

    private void BtnRemoveCollectedItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.DataContext is LocalSendCollectedItem item)
        {
            _vm.RemoveCollectedItem(item);
        }
    }

    private void BtnNextStep_Click(object sender, RoutedEventArgs e) => _vm.ProceedToStep1();

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (_vm.CurrentStep == 0 && e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (_vm.CurrentStep == 0 && e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths)
            {
                _vm.AddPaths(paths);
            }
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void LstDevices_SelectionChanged(object sender, SelectionChangedEventArgs e) => BtnSend.IsEnabled = LstDevices.SelectedItems.Count > 0;

    private void BtnToggleSelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (LstDevices.SelectedItems.Count == LstDevices.Items.Count)
        {
            LstDevices.UnselectAll();
            BtnToggleSelectAll.Content = "全选";
        }
        else
        {
            LstDevices.SelectAll();
            BtnToggleSelectAll.Content = "取消全选";
        }
    }

    private enum CancelSource { None, Self, Receiver }
    private CancelSource _cancelSource = CancelSource.None;

    private async void BtnSend_Click(object sender, RoutedEventArgs e)
    {
        var selectedDevices = LstDevices.SelectedItems.OfType<LocalSendSendDeviceItem>().ToList();
        if (selectedDevices.Count == 0) return;

        // Switch to Step 2 Progress UI
        _vm.CurrentStep = 2;
        TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_Waiting"];
        PrgBar.Visibility = Visibility.Collapsed;

        EventHandler onSendingStarted = (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_Sending"];
            PrgBar.Visibility = Visibility.Visible;
            TxtSpeed.Visibility = Visibility.Visible;
        }));
        _vm.SendingStarted += onSendingStarted;
        try
        {
            await _vm.StartSendBatchAsync(selectedDevices);
        }
        finally
        {
            _vm.SendingStarted -= onSendingStarted;
        }

        // Update UI states after send batch completes
        BtnCancelOrClose.Content = TranslationManager.Instance["Common_Close"];
        TxtSpeed.Visibility = Visibility.Collapsed;
        TxtFileName.Visibility = Visibility.Visible;
        TxtCounter.Visibility = Visibility.Visible;
        PrgBar.Visibility = Visibility.Visible;

        if (_vm.StatusText.Contains(TranslationManager.Instance["Settings_LocalSend_Completed"]))
        {
            TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_Completed"];
        }
        else if (_cancelSource == CancelSource.Self)
        {
            TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_Canceled"];
        }
        else if (_cancelSource == CancelSource.Receiver || _vm.StatusText.Contains(TranslationManager.Instance["Settings_LocalSend_Declined"]))
        {
            TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_ReceiverCanceled"];
        }
        else if (_vm.StatusText.Contains(TranslationManager.Instance["Settings_LocalSend_Busy"]))
        {
            TxtFileName.Visibility = Visibility.Collapsed;
            TxtCounter.Visibility = Visibility.Collapsed;
            PrgBar.Visibility = Visibility.Collapsed;
            TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_Busy"];
        }
        else
        {
            TxtFileName.Visibility = Visibility.Collapsed;
            TxtCounter.Visibility = Visibility.Collapsed;
            PrgBar.Visibility = Visibility.Collapsed;
            TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_ConnectionError"];
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsSending) return;
        Close();
    }

    private void BtnCancelOrClose_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsSending)
        {
            _cancelSource = CancelSource.Self;
            BtnCancelOrClose.Content = TranslationManager.Instance["Common_Close"];
            TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_Canceled"];
            _vm.CancelCommand.Execute(null);
            return;
        }
        Close();
    }

    public void HandleSessionCanceled(string sessionId) => Dispatcher.BeginInvoke(new Action(() =>
    {
        _cancelSource = CancelSource.Receiver;
        TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_ReceiverCanceled"];
        BtnCancelOrClose.Content = TranslationManager.Instance["Common_Close"];
        _vm.CancelCommand.Execute(null);
    }));

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape || e.SystemKey == Key.Escape)
        {
            e.Handled = true;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_vm.IsSending)
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }
}
