using System.Net;
using System.Net.Http.Headers;
using Messenger.Contracts.Auth;

namespace Messenger.Maui.Services;

public sealed class AuthSessionHandler(
    ISecureSessionStore sessionStore,
    IAuthApi authApi,
    INavigationService navigation) : DelegatingHandler
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var initial = await sessionStore.GetAsync(cancellationToken).ConfigureAwait(false);
        if (initial is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", initial.Session.AccessToken);
        }

        using var retry = await CloneAsync(request, cancellationToken).ConfigureAwait(false);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized || initial is null)
        {
            return response;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await sessionStore.GetAsync(cancellationToken).ConfigureAwait(false);
            if (current is null)
            {
                return response;
            }

            if (current.Session.RefreshToken == initial.Session.RefreshToken)
            {
                try
                {
                    var refreshed = await authApi.RefreshAsync(
                        new RefreshRequest(current.Session.RefreshToken, current.DeviceLabel), cancellationToken).ConfigureAwait(false);
                    current = new ClientSession(refreshed, current.DeviceLabel);
                    await sessionStore.SaveAsync(current, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is HttpRequestException or InvalidDataException)
                {
                    await sessionStore.ClearAsync(cancellationToken).ConfigureAwait(false);
                    await navigation.ShowLoginAsync().ConfigureAwait(false);
                    return response;
                }
            }

            retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", current.Session.AccessToken);
            // Android may drain an unread response body while closing its native stream.
            // Even completed awaits must not let that network I/O run on the UI thread.
            await Task.Run(response.Dispose, CancellationToken.None).ConfigureAwait(false);
            return await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        foreach (var option in request.Options)
        {
            clone.Options.TryAdd(option.Key, option.Value);
        }
        if (request.Content is not null)
        {
            var content = new ByteArrayContent(await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
            foreach (var header in request.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            clone.Content = content;
        }
        return clone;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshLock.Dispose();
        }
        base.Dispose(disposing);
    }
}
