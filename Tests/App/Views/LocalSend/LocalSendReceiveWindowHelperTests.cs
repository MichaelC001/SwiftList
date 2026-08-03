using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftList.App.Views.LocalSend;

namespace SwiftList.App.Tests.Views.LocalSend;

[TestClass]
public sealed class LocalSendReceiveWindowHelperTests
{
    [TestMethod]
    public void FormatSummaryFileName_SingleFile_ReturnsFileName()
    {
        var res = LocalSendReceiveWindowHelper.FormatSummaryFileName("test.apk", 1);
        Assert.AreEqual("test.apk", res);
    }

    [TestMethod]
    public void FormatSummaryFileName_MultiFiles_ReturnsFileNameWithCount()
    {
        var res = LocalSendReceiveWindowHelper.FormatSummaryFileName("test.apk", 3);
        Assert.AreEqual("test.apk (3)", res);
    }

    [TestMethod]
    public void ResolveFolderTarget_InvalidPath_ReturnsEmpty()
    {
        var res = LocalSendReceiveWindowHelper.ResolveFolderTarget(@"C:\NonExistentDir12345\file.txt", null);
        Assert.AreEqual(string.Empty, res);
    }
}
