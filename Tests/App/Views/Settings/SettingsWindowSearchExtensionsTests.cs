using SwiftList.App.ViewModels.Settings;
using SwiftList.App.ViewModels.Settings.Plugins;
using SwiftList.Core;
using SwiftList.PluginSdk.Abstractions;

namespace SwiftList.App.Tests.Views.Settings;

[TestClass]
public sealed class SettingsWindowSearchExtensionsTests
{
    [TestMethod]
    public void BuildAllEntries_IncludesPluginConfigFields()
    {
        var settings = new UserSettings();
        var field = new PluginConfigField
        {
            Key = "TestSettingKey",
            LabelKey = "Settings_TestLabelKey",
            GroupKey = "Settings_TestGroupKey",
            FieldType = ConfigFieldType.Text,
            DefaultValue = "defaultVal"
        };

        var configFieldVm = new PluginConfigFieldViewModel("test_plugin", field, settings, () => { });
        var pluginVm = new PluginInfoViewModel(
            name: "TestPlugin",
            version: "1.0.0",
            dllFileName: "TestPlugin.dll",
            sdkVersion: "1.5.0",
            components: new List<PluginComponentViewModel>(),
            configFields: new List<PluginConfigFieldViewModel> { configFieldVm },
            description: "Test plugin description");

        var settingsVm = new SettingsViewModel();
        settingsVm.Plugins.Plugins.Clear();
        settingsVm.Plugins.Plugins.Add(pluginVm);

        var results = SettingsWindowSearchExtensions.BuildAllEntries(vm: settingsVm);
        var targetItem = results.FirstOrDefault(r => r.Label == "[Settings_TestLabelKey]");

        Assert.IsNotNull(targetItem, "Expected plugin config field label to be indexed.");
        Assert.AreEqual("Plugins", targetItem.Section);
        StringAssert.Contains(targetItem.SectionLabel, "TestPlugin");
        Assert.IsNotNull(targetItem.Reveal, "Expected dynamic reveal metadata for targeting UI element.");
    }
}
