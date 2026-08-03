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
        AltTabExcluder.Attach(this);
        ThemedWindowIconHelper.Apply(this);
        ThemedWindowIconHelper.Apply(TitleBarLogo, this);

        _vm = new LocalSendSendViewModel(initialFiles, initialText);
        DataContext = _vm;

        _vm.SendSuccessCompleted += (_, _) =>
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            timer.Tick += (_, _) => { timer.Stop(); Close(); };
            timer.Start();
        };

        Closed += (_, _) => _vm.Dispose();
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

    private async void BtnSend_Click(object sender, RoutedEventArgs e)
    {
        var selectedDevices = LstDevices.SelectedItems.OfType<LocalSendSendDeviceItem>().ToList();
        if (selectedDevices.Count == 0) return;

        // Switch to Step 2 Progress UI
        GridStep1.Visibility = Visibility.Collapsed;
        GridStep2.Visibility = Visibility.Visible;
        TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_Sending"];

        await _vm.StartSendBatchAsync(selectedDevices);

        // Update UI states after send batch completes
        BtnCancelOrClose.Content = TranslationManager.Instance["Common_Close"];
        if (_vm.StatusText.Contains(TranslationManager.Instance["Settings_LocalSend_Completed"]))
        {
            TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_Completed"];
        }
        else if (_vm.StatusText.Contains(TranslationManager.Instance["Settings_LocalSend_Declined"]))
        {
            TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_Declined"];
        }
        else
        {
            TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_Canceled"];
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
            BtnCancelOrClose.Content = TranslationManager.Instance["Common_Close"];
            TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_Canceled"];
            _vm.CancelCommand.Execute(null);
            return;
        }
        Close();
    }

    public void HandleSessionCanceled(string sessionId) => Dispatcher.BeginInvoke(new Action(() =>
    {
        TxtWindowTitle.Text = TranslationManager.Instance["Settings_LocalSend_Declined"];
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
