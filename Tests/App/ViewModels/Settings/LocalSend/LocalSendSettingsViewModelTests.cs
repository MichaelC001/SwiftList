using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftList.App.ViewModels.Settings.LocalSend;
using SwiftList.Core;
using SwiftList.Core.Services.LocalSend.Models;

namespace SwiftList.App.Tests.ViewModels.Settings.LocalSend;

[TestClass]
public class LocalSendSettingsViewModelTests
{
    [TestMethod]
    public void LocalSendSettingsViewModel_ReadsAndWritesUserSettings()
    {
        var settings = new UserSettings();
        var vm = new LocalSendSettingsViewModel(settings);

        Assert.IsFalse(vm.Enabled);
        vm.Enabled = true;
        vm.DeviceAlias = "Custom-PC";
        vm.Port = 54321;
        vm.QuickSave = true;

        // Before Apply, UserSettings retains original values
        Assert.IsFalse(settings.LocalSend.Enabled);
        Assert.AreNotEqual("Custom-PC", settings.LocalSend.DeviceAlias);

        // After Apply, UserSettings updates to staged values
        vm.Apply();
        Assert.IsTrue(settings.LocalSend.Enabled);
        Assert.AreEqual("Custom-PC", settings.LocalSend.DeviceAlias);
        Assert.AreEqual(54321, settings.LocalSend.Port);
        Assert.IsTrue(settings.LocalSend.QuickSave);
    }

    [TestMethod]
    public void LocalSendSettingsViewModel_DiscoveredDevices_UpdatesObservableCollection()
    {
        var settings = new UserSettings();
        var vm = new LocalSendSettingsViewModel(settings);

        Assert.IsEmpty(vm.DiscoveredDevices);

        var device = new LocalSendDeviceInfo
        {
            Alias = "Phone-1",
            IpAddress = "192.168.1.100"
        };

        vm.AddDiscoveredDevice(device);

        Assert.HasCount(1, vm.DiscoveredDevices);
        Assert.AreEqual("Phone-1", vm.DiscoveredDevices[0].Alias);
    }
}
