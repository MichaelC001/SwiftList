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

    [TestMethod]
    public void ActivateResult_SwitchesSelectedConfigGroup_WhenFieldBelongsToGroupTab()
    {
        var settings = new UserSettings();
        var groupField1 = new PluginConfigField { Key = "g1", LabelKey = "Group1Key", FieldType = ConfigFieldType.Group, DefaultValue = "" };
        var groupField2 = new PluginConfigField { Key = "g2", LabelKey = "Group2Key", FieldType = ConfigFieldType.Group, DefaultValue = "" };
        var subField2 = new PluginConfigField { Key = "sub2", LabelKey = "Sub2Key", FieldType = ConfigFieldType.Text, DefaultValue = "" };

        var g1Vm = new PluginConfigFieldViewModel("test_plugin", groupField1, settings, () => { });
        var g2Vm = new PluginConfigFieldViewModel("test_plugin", groupField2, settings, () => { });
        var sub2Vm = new PluginConfigFieldViewModel("test_plugin", subField2, settings, () => { });
        g2Vm.Children.Add(sub2Vm);

        var pluginVm = new PluginInfoViewModel(
            name: "TestPlugin",
            version: "1.0.0",
            dllFileName: "TestPlugin.dll",
            sdkVersion: "1.5.0",
            components: new List<PluginComponentViewModel>(),
            configFields: new List<PluginConfigFieldViewModel> { g1Vm, g2Vm },
            description: "Test plugin description");

        var settingsVm = new SettingsViewModel();
        settingsVm.Plugins.Plugins.Clear();
        settingsVm.Plugins.Plugins.Add(pluginVm);

        var results = SettingsWindowSearchExtensions.BuildAllEntries(vm: settingsVm);
        var sub2Item = results.FirstOrDefault(r => r.Label == "[Sub2Key]");

        Assert.IsNotNull(sub2Item);
        sub2Item.Activate?.Invoke(settingsVm);

        Assert.AreEqual(pluginVm, settingsVm.Plugins.SelectedPlugin);
        Assert.IsTrue(pluginVm.IsConfigTab);
        Assert.AreEqual(g2Vm, pluginVm.SelectedConfigGroup);
    }
}
