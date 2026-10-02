using System.Net;
using Messenger.Contracts.Auth;
using Messenger.Maui.Services;
using static Messenger.Maui.Tests.Auth.AuthViewModelTests;

namespace Messenger.Maui.Tests.Services;

public sealed class AuthSessionHandlerTests
{
    [Fact]
    public async Task Concurrent_unauthorized_responses_refresh_once_and_retry_with_rotated_tokens()
    {
        var store = new MemorySessionStore
        {
            Current = new ClientSession(RecordingAuthApi.CreateSession("old"), "Android")
        };
        var api = new RecordingAuthApi { RefreshDelay = TimeSpan.FromMilliseconds(50) };
        var navigation = new RecordingNavigation();
        var server = new TokenAwareServerHandler("access-new");
        using var client = new HttpClient(new AuthSessionHandler(store, api, navigation) { InnerHandler = server });

        var responses = await Task.WhenAll(client.GetAsync("https://example.test/one"), client.GetAsync("https://example.test/two"));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(1, api.RefreshCount);
        Assert.Equal("refresh-new", store.Current?.Session.RefreshToken);
        Assert.Equal(4, server.RequestCount);
    }

    [Fact]
    public async Task Refresh_failure_clears_session_and_opens_login_without_a_second_retry()
    {
        var store = new MemorySessionStore
        {
            Current = new ClientSession(RecordingAuthApi.CreateSession("old"), "iPhone")
        };
        var api = new RecordingAuthApi { RefreshFailure = new HttpRequestException("refresh rejected") };
        var navigation = new RecordingNavigation();
        var server = new TokenAwareServerHandler("never-matches");
        using var client = new HttpClient(new AuthSessionHandler(store, api, navigation) { InnerHandler = server });

        var response = await client.GetAsync("https://example.test/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(store.Current);
        Assert.Equal(1, navigation.LoginCount);
        Assert.Equal(1, server.RequestCount);
    }

    private sealed class TokenAwareServerHandler(string acceptedToken) : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => _requestCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            var status = request.Headers.Authorization?.Parameter == acceptedToken
                ? HttpStatusCode.OK
                : HttpStatusCode.Unauthorized;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
