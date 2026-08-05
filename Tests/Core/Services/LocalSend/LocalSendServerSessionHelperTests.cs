using SwiftList.Core.Services.LocalSend;
using SwiftList.Core.Services.LocalSend.Models;

namespace SwiftList.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendServerSessionHelperTests
{
    [TestMethod]
    public async Task RequestAcceptanceAsync_NoHandler_ReturnsFalse()
    {
        var server = new LocalSendServer();
        var dto = new PrepareUploadRequestDto
        {
            Info = new LocalSendDeviceInfo { Alias = "TestDevice" },
            Files = new Dictionary<string, LocalSendFileDto>()
        };

        var res = await LocalSendServerSessionHelper.RequestAcceptanceAsync(server, "s1", dto);
        Assert.IsFalse(res.Accepted);
        Assert.IsNull(res.CustomDir);
        Assert.IsNull(res.SelectedFileIds);
    }
}
