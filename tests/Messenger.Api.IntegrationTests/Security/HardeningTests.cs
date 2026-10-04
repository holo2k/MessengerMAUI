using System.Net;
using System.Net.Http.Json;
using Messenger.Api.IntegrationTests.Auth;
using Messenger.Api.Middleware;
using Messenger.Contracts.Auth;
using Messenger.Contracts.Chats;
using Messenger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace Messenger.Api.IntegrationTests.Security;

public sealed class HardeningTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").WithDatabase("messenger_security_tests").WithUsername("messenger").WithPassword("messenger-tests-only").Build();
    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Empty_401_404_and_429_have_stable_problem_codes_and_correlation_ids()
    {
        await using var factory = new MessengerApiFactory(_postgres.GetConnectionString(), "Development", 1);
        var client = factory.CreateClient();
        await AssertProblem(client, await client.GetAsync("/api/me/"), HttpStatusCode.Unauthorized, "unauthorized");
        await AssertProblem(client, await client.GetAsync("/missing"), HttpStatusCode.NotFound, "not_found");
        await client.PostAsJsonAsync("/api/auth/challenges", new { phone = "+79990000901", defaultRegion = "RU", purpose = "register" });
        await AssertProblem(client, await client.PostAsJsonAsync("/api/auth/challenges", new { phone = "+79990000902", defaultRegion = "RU", purpose = "register" }), HttpStatusCode.TooManyRequests, "too_many_requests");
    }

    [Fact]
    public async Task Production_redirects_http_and_emits_hsts_on_https()
    {
        await using var factory = new MessengerApiFactory(_postgres.GetConnectionString(), "Production", 100, skipSmsValidation: true);
        var plain = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("http://localhost") });
        var redirect = await plain.GetAsync("/"); Assert.Equal(HttpStatusCode.TemporaryRedirect, redirect.StatusCode); Assert.Equal("https", redirect.Headers.Location?.Scheme);
        var secure = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var ok = await secure.GetAsync("/"); ok.EnsureSuccessStatusCode(); Assert.True(ok.Headers.Contains("Strict-Transport-Security"));

        using var proxiedRequest = new HttpRequestMessage(HttpMethod.Get, "/"); proxiedRequest.Headers.Add("X-Forwarded-Proto", "https");
        var proxied = await plain.SendAsync(proxiedRequest); proxied.EnsureSuccessStatusCode(); Assert.True(proxied.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Cors_preflight_allows_only_configured_origin()
    {
        await using var factory = new MessengerApiFactory(
            _postgres.GetConnectionString(),
            "Development",
            100,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = "https://trusted.example"
            });
        var client = factory.CreateClient();

        using var allowedRequest = CreatePreflight("https://trusted.example");
        using var allowed = await client.SendAsync(allowedRequest);
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        Assert.Equal("https://trusted.example", allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", allowed.Headers.GetValues("Access-Control-Allow-Credentials").Single());

        using var rejectedRequest = CreatePreflight("https://untrusted.example");
        using var rejected = await client.SendAsync(rejectedRequest);
        Assert.Equal(HttpStatusCode.NoContent, rejected.StatusCode);
        Assert.False(rejected.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(rejected.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task Domain_403_409_and_422_keep_problem_contract_and_development_exposes_openapi()
    {
        await using var factory = new MessengerApiFactory(_postgres.GetConnectionString(), "Development", 100);
        await using (var scope = factory.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<MessengerDbContext>().Database.MigrateAsync();
        var client = factory.CreateClient();
        var invalid = await Post<ChallengeResponse>(client, "/api/auth/challenges", new ChallengeRequest("+79990000911", "RU", "register"));
        await AssertProblem(client, await client.PostAsJsonAsync("/api/auth/register", new CompleteChallengeRequest(invalid.ChallengeId, "000000", "Tests")), HttpStatusCode.UnprocessableEntity, "invalid_code");
        var alice = await Register(client, "+79990000912"); var bob = await Register(client, "+79990000913");
        var duplicate = await Post<ChallengeResponse>(client, "/api/auth/challenges", new ChallengeRequest("+79990000912", "RU", "register"));
        await AssertProblem(client, await client.PostAsJsonAsync("/api/auth/register", new CompleteChallengeRequest(duplicate.ChallengeId, "111111", "Tests")), HttpStatusCode.Conflict, "phone_already_registered");
        Authorize(client, alice); var group = await Post<ChatResponse>(client, "/api/chats/groups", new CreateGroupChatRequest("Группа", [bob.UserId]));
        Authorize(client, bob); await AssertProblem(client, await client.PatchAsJsonAsync($"/api/chats/{group.Id}", new UpdateGroupChatRequest("Чужое", null)), HttpStatusCode.Forbidden, "forbidden");
        client.DefaultRequestHeaders.Authorization = null; var openApi = await client.GetAsync("/openapi/v1.json"); openApi.EnsureSuccessStatusCode();
    }

    [Fact]
    public void Sensitive_values_are_redacted_before_logging()
    {
        var text = SensitiveDataLoggingFilter.Redact("Authorization: Bearer abc accessToken=secret refreshToken=hidden password=hunter2");
        Assert.DoesNotContain("abc", text); Assert.DoesNotContain("secret", text); Assert.DoesNotContain("hidden", text); Assert.DoesNotContain("hunter2", text);
    }

    [Fact]
    public async Task Request_logging_omits_query_headers_and_bodies()
    {
        var logs = new RecordingLoggerProvider();
        await using var baseFactory = new MessengerApiFactory(_postgres.GetConnectionString(), "Development", 100);
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(logs)));
        var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "top-secret-token");
        await client.GetAsync("/missing?accessToken=query-secret");
        Assert.Contains(logs.Messages, value => value.Contains("HTTP GET /missing responded", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Messages, value => value.Contains("query-secret", StringComparison.Ordinal) || value.Contains("top-secret-token", StringComparison.Ordinal));
    }

    private static async Task AssertProblem(HttpClient client, HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode); Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(); Assert.Equal(code, body!["code"].ToString());
        Assert.True(response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values)); Assert.Equal(values.Single(), body["correlationId"].ToString());
    }
    private static async Task<AuthSessionResponse> Register(HttpClient client, string phone) { client.DefaultRequestHeaders.Authorization = null; var challenge = await Post<ChallengeResponse>(client, "/api/auth/challenges", new ChallengeRequest(phone, "RU", "register")); return await Post<AuthSessionResponse>(client, "/api/auth/register", new CompleteChallengeRequest(challenge.ChallengeId, "111111", "Tests")); }
    private static void Authorize(HttpClient client, AuthSessionResponse session) => client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken);
    private static async Task<T> Post<T>(HttpClient client, string uri, object body) { using var response = await client.PostAsJsonAsync(uri, body); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<T>())!; }
    private static HttpRequestMessage CreatePreflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/challenges");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        return request;
    }
    private sealed class RecordingLoggerProvider : ILoggerProvider, ILogger
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Enqueue(formatter(state, exception));
        public void Dispose() { }
    }
}
