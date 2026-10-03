using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Messenger.Contracts.Auth;
using Messenger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Messenger.Api.IntegrationTests.Auth;

public sealed class AuthEndpointTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("messenger_api_tests")
        .WithUsername("messenger")
        .WithPassword("messenger-tests-only")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Register_login_refresh_logout_and_logout_all_routes_work()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateClient();
        var registered = await RegisterAsync(client, "+79990000001", "Android");

        var loginChallenge = await RequestChallengeAsync(client, "+79990000001", "login");
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new CompleteChallengeRequest(
            loginChallenge.ChallengeId, "111111", "iPhone"));
        var loggedIn = await loginResponse.Content.ReadFromJsonAsync<AuthSessionResponse>();
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.NotNull(loggedIn);

        var refreshResponse = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(
            loggedIn.RefreshToken, "iPhone"));
        var refreshed = await refreshResponse.Content.ReadFromJsonAsync<AuthSessionResponse>();
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        Assert.NotNull(refreshed);
        Assert.NotEqual(loggedIn.RefreshToken, refreshed.RefreshToken);

        var replay = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(
            loggedIn.RefreshToken, "iPhone"));
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);

        var revokedReplacement = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(
            refreshed.RefreshToken, "iPhone"));
        Assert.Equal(HttpStatusCode.Unauthorized, revokedReplacement.StatusCode);

        var logout = await client.PostAsJsonAsync("/api/auth/logout", new RefreshRequest(
            registered.RefreshToken, "Android"));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var secondLoginChallenge = await RequestChallengeAsync(client, "+79990000001", "login");
        var secondLogin = await client.PostAsJsonAsync("/api/auth/login", new CompleteChallengeRequest(
            secondLoginChallenge.ChallengeId, "111111", "Tablet"));
        var secondSession = await secondLogin.Content.ReadFromJsonAsync<AuthSessionResponse>();
        Assert.NotNull(secondSession);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secondSession.AccessToken);
        var logoutAll = await client.PostAsync("/api/auth/logout-all", null);
        Assert.Equal(HttpStatusCode.NoContent, logoutAll.StatusCode);
    }

    [Fact]
    public async Task Concurrent_registration_enforces_unique_phone_blind_index()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateClient();
        var first = await RequestChallengeAsync(client, "8 (999) 000-00-02", "register");
        var second = await RequestChallengeAsync(client, "+79990000002", "register");

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/auth/register", new CompleteChallengeRequest(first.ChallengeId, "111111", "A")),
            client.PostAsJsonAsync("/api/auth/register", new CompleteChallengeRequest(second.ChallengeId, "111111", "B")));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Invalid_code_returns_problem_details_with_stable_code_and_correlation_id()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateClient();
        var challenge = await RequestChallengeAsync(client, "+79990000003", "register");

        var response = await client.PostAsJsonAsync("/api/auth/register", new CompleteChallengeRequest(
            challenge.ChallengeId, "000000", "Android"));
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("invalid_code", problem?["code"].ToString());
        Assert.False(string.IsNullOrWhiteSpace(problem?["correlationId"].ToString()));
    }

    [Fact]
    public async Task Challenge_endpoint_is_rate_limited()
    {
        await using var factory = await CreateFactoryAsync(challengePermitLimit: 2);
        var client = factory.CreateClient();

        await RequestChallengeAsync(client, "+79990000010", "register");
        await RequestChallengeAsync(client, "+79990000011", "register");
        var limited = await client.PostAsJsonAsync("/api/auth/challenges", new ChallengeRequest(
            "+79990000012", "RU", "register"));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task Code_attempts_are_rate_limited()
    {
        await using var factory = await CreateFactoryAsync(codePermitLimit: 2);
        var client = factory.CreateClient();
        var challenge = await RequestChallengeAsync(client, "+79990000013", "register");
        var request = new CompleteChallengeRequest(challenge.ChallengeId, "000000", "Android");

        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await client.PostAsJsonAsync("/api/auth/register", request)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await client.PostAsJsonAsync("/api/auth/register", request)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await client.PostAsJsonAsync("/api/auth/register", request)).StatusCode);
    }

    [Fact]
    public void Production_startup_rejects_development_sms_sender()
    {
        using var factory = new MessengerApiFactory(
            _postgres.GetConnectionString(),
            "Production",
            challengePermitLimit: 10);

        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }

    private async Task<MessengerApiFactory> CreateFactoryAsync(
        int challengePermitLimit = 100,
        int codePermitLimit = 100)
    {
        var factory = new MessengerApiFactory(
            _postgres.GetConnectionString(),
            "Development",
            challengePermitLimit,
            codePermitLimit);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();
        await context.Database.MigrateAsync();
        return factory;
    }

    private static async Task<AuthSessionResponse> RegisterAsync(HttpClient client, string phone, string device)
    {
        var challenge = await RequestChallengeAsync(client, phone, "register");
        var response = await client.PostAsJsonAsync("/api/auth/register", new CompleteChallengeRequest(
            challenge.ChallengeId, "111111", device));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthSessionResponse>())!;
    }

    private static async Task<ChallengeResponse> RequestChallengeAsync(
        HttpClient client,
        string phone,
        string purpose)
    {
        var response = await client.PostAsJsonAsync("/api/auth/challenges", new ChallengeRequest(
            phone, "RU", purpose));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChallengeResponse>())!;
    }
}

public sealed class MessengerApiFactory(
    string connectionString,
    string environment,
    int challengePermitLimit,
    int codePermitLimit = 100,
    string? minioEndpoint = null,
    string? minioAccessKey = null,
    string? minioSecretKey = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Messenger"] = connectionString,
                ["Encryption:ActiveKeyVersion"] = "1",
                ["Encryption:Keys:1"] = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray()),
                ["BlindIndex:Key"] = Convert.ToBase64String(Enumerable.Repeat((byte)2, 32).ToArray()),
                ["ChallengeHash:Key"] = Convert.ToBase64String(Enumerable.Repeat((byte)3, 32).ToArray()),
                ["Jwt:Issuer"] = "messenger-tests",
                ["Jwt:Audience"] = "messenger-tests",
                ["Jwt:SigningKey"] = Convert.ToBase64String(Enumerable.Repeat((byte)4, 32).ToArray()),
                ["Sms:Provider"] = "Development",
                ["RateLimits:ChallengePermitLimit"] = challengePermitLimit.ToString(),
                ["RateLimits:CodePermitLimit"] = codePermitLimit.ToString()
                , ["Minio:Endpoint"] = minioEndpoint ?? "http://localhost:9000"
                , ["Minio:AccessKey"] = minioAccessKey ?? "messenger"
                , ["Minio:SecretKey"] = minioSecretKey ?? "messenger-local-only"
                , ["Minio:Bucket"] = $"messenger-tests-{Guid.NewGuid():N}"
            }));
    }
}
