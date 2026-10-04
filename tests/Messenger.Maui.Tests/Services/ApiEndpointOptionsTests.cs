using Messenger.Maui.Services;

namespace Messenger.Maui.Tests.Services;

public sealed class ApiEndpointOptionsTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void All_builds_default_to_deployed_https(bool isAndroid, bool isDebug)
    {
        var options = ApiEndpointOptions.Create(isAndroid, isDebug);

        Assert.Equal(new Uri("https://api.projectdomain.ru/"), options.ApiBaseAddress);
        Assert.Equal(new Uri("https://api.projectdomain.ru/hubs/chat"), options.ChatHubAddress);
    }

    [Fact]
    public void Explicit_https_override_is_normalized()
    {
        var options = ApiEndpointOptions.Create(true, true, "https://test.example/api");

        Assert.Equal(new Uri("https://test.example/api/"), options.ApiBaseAddress);
        Assert.Equal(new Uri("https://test.example/api/hubs/chat"), options.ChatHubAddress);
    }

    [Fact]
    public void Android_debug_accepts_explicit_emulator_http_override()
    {
        var options = ApiEndpointOptions.Create(true, true, "http://10.0.2.2:5192");

        Assert.Equal(new Uri("http://10.0.2.2:5192/"), options.ApiBaseAddress);
    }

    [Theory]
    [InlineData("http://api.projectdomain.ru/")]
    [InlineData("http://10.0.2.2:5192/")]
    [InlineData("not-a-uri")]
    public void Unsafe_or_invalid_release_override_is_rejected(string address)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ApiEndpointOptions.Create(isAndroid: true, isDebug: false, address));

        Assert.Contains("HTTPS", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
