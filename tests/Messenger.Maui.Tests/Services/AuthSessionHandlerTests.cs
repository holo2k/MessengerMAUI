using System.Net;
using Messenger.Contracts.Auth;
using Messenger.Maui.Services;
using static Messenger.Maui.Tests.Auth.AuthViewModelTests;

namespace Messenger.Maui.Tests.Services;

public sealed class AuthSessionHandlerTests
{
    [Fact]
    public async Task Startup_token_refresh_closes_the_unread_response_away_from_the_ui_thread()
    {
        var store = new MemorySessionStore
        {
            Current = new ClientSession(RecordingAuthApi.CreateSession("old"), "Android")
        };
        var server = new UnreadResponseServerHandler();
        using var client = new HttpClient(new AuthSessionHandler(store, new RecordingAuthApi(), new RecordingNavigation())
        {
            InnerHandler = server
        });
        var started = new TaskCompletionSource<Task<HttpResponseMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiThread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            server.UiThreadId = Environment.CurrentManagedThreadId;
            try
            {
                started.SetResult(client.GetAsync("https://example.test/api/chat-folders", HttpCompletionOption.ResponseHeadersRead));
            }
            catch (Exception exception)
            {
                started.SetException(exception);
            }
        }) { IsBackground = true };
        uiThread.Start();

        using var response = await (await started.Task.WaitAsync(TimeSpan.FromSeconds(5))).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(server.UnreadContent.WasDisposed);
        Assert.NotEqual(server.UiThreadId, server.UnreadContent.DisposeThreadId);
        Assert.Equal("refresh-new", store.Current?.Session.RefreshToken);
    }

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

    private sealed class UnreadResponseServerHandler : HttpMessageHandler
    {
        public int UiThreadId { get; set; }
        public ThreadSensitiveContent UnreadContent { get; private set; } = null!;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Authorization?.Parameter == "access-new")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

            UnreadContent = new ThreadSensitiveContent(UiThreadId);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = UnreadContent });
        }
    }

    private sealed class ThreadSensitiveContent(int uiThreadId) : HttpContent
    {
        public bool WasDisposed { get; private set; }
        public int DisposeThreadId { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.CompletedTask;
        protected override bool TryComputeLength(out long length) { length = 0; return true; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeThreadId = Environment.CurrentManagedThreadId;
                if (DisposeThreadId == uiThreadId)
                    throw new InvalidOperationException("Closing an unread Android response can perform network I/O on the UI thread.");
                WasDisposed = true;
            }
            base.Dispose(disposing);
        }
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
