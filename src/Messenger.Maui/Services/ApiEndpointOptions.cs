namespace Messenger.Maui.Services;

public sealed record ApiEndpointOptions(Uri ApiBaseAddress)
{
    public Uri ChatHubAddress => new(ApiBaseAddress, "hubs/chat");

    public static ApiEndpointOptions Create(bool isAndroid, bool isDebug, string? configuredBaseAddress = null)
    {
        const string deployedAddress = "https://api.projectdomain.ru/";
        var address = string.IsNullOrWhiteSpace(configuredBaseAddress)
            ? deployedAddress
            : configuredBaseAddress.Trim();
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("The API base address must be an absolute HTTPS URI.");
        }

        var isAndroidEmulatorHttp = isAndroid && isDebug &&
            uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            uri.Host.Equals("10.0.2.2", StringComparison.OrdinalIgnoreCase);
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) && !isAndroidEmulatorHttp)
        {
            throw new InvalidOperationException("The API base address must use HTTPS except for an explicit Android Debug emulator override.");
        }
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException("The API base address must be a clean HTTPS URI without credentials, query, or fragment.");
        }

        var path = uri.AbsolutePath.EndsWith('/') ? uri.AbsolutePath : $"{uri.AbsolutePath}/";
        return new ApiEndpointOptions(new UriBuilder(uri) { Path = path }.Uri);
    }
}
