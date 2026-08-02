using System.Net;
using SwiftList.Core.Services.LocalSend;

namespace SwiftList.Core.Tests.Services.LocalSend;

[TestClass]
public class LocalSendServerHelperTests
{
    [TestMethod]
    public void FormatIpAddress_IPv4MappedIPv6_UnmapsToStandardIPv4()
    {
        var mappedIp = IPAddress.Parse("::ffff:192.168.1.100");

        var result = LocalSendServerHelper.FormatIpAddress(mappedIp);

        Assert.AreEqual("192.168.1.100", result);
    }

    [TestMethod]
    public void FormatIpAddress_StandardIPv4_ReturnsSameString()
    {
        var ipv4 = IPAddress.Parse("192.168.1.50");

        var result = LocalSendServerHelper.FormatIpAddress(ipv4);

        Assert.AreEqual("192.168.1.50", result);
    }

    [TestMethod]
    public void FormatIpAddress_StandardIPv6_ReturnsStandardString()
    {
        var ipv6 = IPAddress.Parse("fe80::1");

        var result = LocalSendServerHelper.FormatIpAddress(ipv6);

        Assert.AreEqual("fe80::1", result);
    }

    [TestMethod]
    public async Task WriteResponseAsync_StatusOK_WritesValidHttpResponseHeaders()
    {
        using var ms = new MemoryStream();

        await LocalSendServerHelper.WriteResponseAsync(ms, 200).ConfigureAwait(false);

        var output = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        StringAssert.Contains(output, "HTTP/1.1 200 OK\r\n");
        StringAssert.Contains(output, "Content-Length: 0\r\n");
    }

    [TestMethod]
    public async Task WriteResponseAsync_WithJsonBody_WritesContentLengthAndJson()
    {
        using var ms = new MemoryStream();
        var json = "{\"alias\":\"test\"}";

        await LocalSendServerHelper.WriteResponseAsync(ms, 200, json).ConfigureAwait(false);

        var output = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        StringAssert.Contains(output, "HTTP/1.1 200 OK\r\n");
        StringAssert.Contains(output, "Content-Type: application/json\r\n");
        StringAssert.Contains(output, "Content-Length: 16\r\n");
        StringAssert.Contains(output, json);
    }
}
