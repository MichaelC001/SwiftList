using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SwiftList.App.Helpers.Visuals;
using SwiftList.App.Services;
using SwiftList.App.Services.Theme;
using SwiftList.Core.Services.LocalSend;
using SwiftList.Core.Services.LocalSend.Models;

namespace SwiftList.App.Views.LocalSend;

public enum LocalSendReceiveResult
{
    Decline,
    AcceptDefault,
    AcceptCustomDir
}

public sealed class LocalSendReceiveFileItem
{
    public required string FileName { get; init; }
    public required string SizeText { get; init; }
}

public partial class LocalSendReceiveRequestWindow : Window
{
    public LocalSendReceiveResult Result { get; private set; } = LocalSendReceiveResult.Decline;
    public string? CustomDirectory { get; private set; }

    public LocalSendReceiveRequestWindow(PrepareUploadRequestDto dto)
    {
        InitializeComponent();

        SystemMenuBlocker.Attach(this);
        AltTabExcluder.Attach(this);
        ThemedWindowIconHelper.Apply(this);
        ThemedWindowIconHelper.Apply(TitleBarLogo, this);

        PopulateData(dto);
    }

    private void PopulateData(PrepareUploadRequestDto dto)
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

    private void BtnDecline_Click(object sender, RoutedEventArgs e)
    {
        Result = LocalSendReceiveResult.Decline;
        DialogResult = false;
        Close();
    }

    private void BtnAcceptDefault_Click(object sender, RoutedEventArgs e)
    {
        Result = LocalSendReceiveResult.AcceptDefault;
        DialogResult = true;
        Close();
    }

    private void BtnSaveTo_Click(object sender, RoutedEventArgs e)
    {
        var title = TranslationManager.Instance["Settings_LocalSend_UploadRequestTitle"];
        var dialog = new OpenFolderDialog { Title = title };
        if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            CustomDirectory = dialog.FolderName;
            Result = LocalSendReceiveResult.AcceptCustomDir;
            DialogResult = true;
            Close();
        }
    }
}
