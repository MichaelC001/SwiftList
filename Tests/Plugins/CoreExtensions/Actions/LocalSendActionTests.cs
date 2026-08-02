using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftList.Plugins.CoreExtensions.Actions;
using SwiftList.PluginSdk.Abstractions;

namespace SwiftList.Plugins.CoreExtensions.Tests.Actions;

[TestClass]
public class LocalSendActionTests
{
    [TestMethod]
    public void CanExecute_EmptyList_ReturnsFalse()
    {
        var action = new LocalSendAction();
        var canExec = action.CanExecute(Array.Empty<ISearchResult>());
        Assert.IsFalse(canExec);
    }

    [TestMethod]
    public void DisplayName_IsNotEmpty()
    {
        var action = new LocalSendAction();
        Assert.IsFalse(string.IsNullOrWhiteSpace(action.DisplayName));
    }
}
