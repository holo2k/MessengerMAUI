using Messenger.Maui.Services;

namespace Messenger.Maui.Tests.Services;

public sealed class ApiEndpointOptionsTests
{
    [Fact]
    public void Android_debug_uses_emulator_host_over_local_http()
    {
        var options = ApiEndpointOptions.Create(isAndroid: true, isDebug: true);

        Assert.Equal(new Uri("http://10.0.2.2:5192/"), options.ApiBaseAddress);
        Assert.Equal(new Uri("http://10.0.2.2:5192/hubs/chat"), options.ChatHubAddress);
    }

    [Fact]
    public void Release_endpoints_remain_https()
    {
        var android = ApiEndpointOptions.Create(isAndroid: true, isDebug: false);
        var local = ApiEndpointOptions.Create(isAndroid: false, isDebug: false);

        Assert.Equal("https", android.ApiBaseAddress.Scheme);
        Assert.Equal("https", local.ApiBaseAddress.Scheme);
    }
}
