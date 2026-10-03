namespace Messenger.Maui.Services;

public sealed record ApiEndpointOptions(Uri ApiBaseAddress)
{
    public Uri ChatHubAddress => new(ApiBaseAddress, "hubs/chat");

    public static ApiEndpointOptions Create(bool isAndroid, bool isDebug)
    {
        var address = (isAndroid, isDebug) switch
        {
            (true, true) => "http://10.0.2.2:5192/",
            (true, false) => "https://10.0.2.2:7106/",
            (_, true) => "http://localhost:5192/",
            _ => "https://localhost:7106/"
        };
        return new ApiEndpointOptions(new Uri(address));
    }
}
