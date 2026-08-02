using SwiftList.Core.Services.LocalSend;
using SwiftList.Core.Services.LocalSend.Models;

namespace SwiftList.Core.Tests.Services.LocalSend;

[TestClass]
public class LocalSendAliasGeneratorTests
{
    [TestMethod]
    public void GenerateRandomAlias_ChineseCulture_ReturnsDeCombination()
    {
        var alias = LocalSendAliasGenerator.GenerateRandomAlias("zh-CN");

        Assert.IsFalse(string.IsNullOrWhiteSpace(alias));
        StringAssert.Contains(alias, "的");
    }

    [TestMethod]
    public void GenerateRandomAlias_EnglishCulture_ReturnsSpaceSeparatedWords()
    {
        var alias = LocalSendAliasGenerator.GenerateRandomAlias("en-US");

        Assert.IsFalse(string.IsNullOrWhiteSpace(alias));
        StringAssert.Contains(alias, " ");
    }

    [TestMethod]
    public void GenerateRandomAlias_JapaneseCulture_ReturnsValidAlias()
    {
        var alias = LocalSendAliasGenerator.GenerateRandomAlias("ja-JP");

        Assert.IsFalse(string.IsNullOrWhiteSpace(alias));
    }

    [TestMethod]
    public void GenerateRandomAlias_MultipleCalls_GeneratesVariedNames()
    {
        var results = new HashSet<string>();
        for (var i = 0; i < 20; i++)
        {
            results.Add(LocalSendAliasGenerator.GenerateRandomAlias("zh-CN"));
        }

        Assert.IsGreaterThan(1, results.Count);
    }

    [TestMethod]
    public void GetLocalDeviceHashtag_ReturnsValidHashtag()
    {
        var hashtag = LocalSendServerHelper.GetLocalDeviceHashtag();

        Assert.IsFalse(string.IsNullOrWhiteSpace(hashtag));
        StringAssert.StartsWith(hashtag, "#");
    }

    [TestMethod]
    public void CheckAndNotifyTextReceived_ValidText_TriggersEvent()
    {
        using var server = new LocalSendServer();
        var triggered = false;
        var isLinkResult = false;
        server.TextReceived += (s, e) =>
        {
            triggered = true;
            isLinkResult = e.IsLink;
        };

        var dto = new PrepareUploadRequestDto
        {
            Info = new LocalSendDeviceInfo(),
            Files = new Dictionary<string, LocalSendFileDto>
            {
                ["f1"] = new LocalSendFileDto
                {
                    Id = "f1",
                    FileName = "sample.txt",
                    FileType = "text",
                    Preview = "https://localsend.org"
                }
            }
        };

        var result = LocalSendServerHelper.CheckAndNotifyTextReceived(server, dto, "f1", "sample.txt", "TestSender");

        Assert.IsTrue(result);
        Assert.IsTrue(triggered);
        Assert.IsTrue(isLinkResult);
    }
}
