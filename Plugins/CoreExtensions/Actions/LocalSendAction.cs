using System.Windows.Media;
using SwiftList.PluginSdk;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Helpers;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.CoreExtensions.Actions;

public class LocalSendAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Action_BuiltinGroup");

    public string DisplayName => TranslationService.Get("Action_LocalSend");

    public string Description => TranslationService.Get("Action_LocalSend");

    public string Hotkey => "Ctrl+Shift+L";

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5",
        "TextPrimary");

    public bool CanExecute(IReadOnlyList<ISearchResult> results) => results.Count > 0;

    public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view)
    {
        try
        {
            var files = results.Select(r => r.FullPath).Where(p => !string.IsNullOrEmpty(p) && (System.IO.File.Exists(p) || System.IO.Directory.Exists(p))).ToList();
            var text = files.Count == 0 ? string.Join(Environment.NewLine, results.Select(r => r.Name).Where(t => !string.IsNullOrEmpty(t))) : null;

            LocalSendTransferService.OpenSendWindow(files, text);
        }
        catch (Exception ex)
        {
            Logger.Log($"[LocalSendAction] Failed to execute LocalSend action: {ex.Message}", LogLevel.Error);
        }
    }
}
