using System.Windows;
using System.Windows.Input;
using SwiftList.App.Helpers.Visuals;
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

        Closed += (_, _) => _vm.Dispose();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
