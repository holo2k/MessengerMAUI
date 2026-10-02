using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Messenger.Api.IntegrationTests.Auth;
using Messenger.Application.Users;
using Messenger.Contracts.Auth;
using Messenger.Contracts.Users;
using Messenger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace Messenger.Api.IntegrationTests.Users;

public sealed class ProfileEndpointTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("messenger_profile_tests")
        .WithUsername("messenger")
        .WithPassword("messenger-tests-only")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Profile_privacy_and_email_two_factor_pending_login_flow_work()
    {
        var email = new RecordingEmailSender();
        await using var factory = await CreateFactoryAsync(email);
        var client = factory.CreateClient();
        var session = await RegisterAsync(client, "+79990000201");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsJsonAsync("/api/me", new UpdateMeRequest(
            "Иван", "Иванов", "Ivan", "bio"))).StatusCode);
        var me = await client.GetFromJsonAsync<MeResponse>("/api/me");
        Assert.Equal("Ivan", me?.Username);
        Assert.Equal("+79990000201", me?.Phone);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/me/privacy", new PrivacyRequest(
            "contacts", "everybody", "nobody"))).StatusCode);
        var privacy = await client.GetFromJsonAsync<PrivacyResponse>("/api/me/privacy");
        Assert.Equal("contacts", privacy?.Phone);
        Assert.Equal("everybody", privacy?.Avatar);
        Assert.Equal("nobody", privacy?.LastSeen);

        var phoneChallenge = await (await client.PostAsJsonAsync("/api/me/phone/challenges",
            new BeginPhoneChangeRequest("+79990000999", "RU"))).Content.ReadFromJsonAsync<EmailChallengeResponse>();
        Assert.NotNull(phoneChallenge);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/me/phone",
            new ChangePhoneRequest(phoneChallenge.ChallengeId, "111111"))).StatusCode);
        me = await client.GetFromJsonAsync<MeResponse>("/api/me");
        Assert.Equal("+79990000999", me?.Phone);

        var setup = await (await client.PostAsJsonAsync("/api/me/2fa/email", new BeginEmailTwoFactorRequest(
            "user@example.test"))).Content.ReadFromJsonAsync<EmailChallengeResponse>();
        Assert.NotNull(setup);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/me/2fa/email/confirm",
            new ConfirmEmailTwoFactorRequest(setup.ChallengeId, email.LastCode!))).StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var loginChallenge = await RequestChallengeAsync(client, "+79990000999", "login");
        var login = await client.PostAsJsonAsync("/api/auth/login", new CompleteChallengeRequest(
            loginChallenge.ChallengeId, "111111", "iPhone"));
        var pending = await login.Content.ReadFromJsonAsync<PendingTwoFactorResponse>();
        Assert.Equal(HttpStatusCode.Accepted, login.StatusCode);
        Assert.NotNull(pending);

        var confirmations = await Task.WhenAll(
            client.PostAsJsonAsync("/api/auth/2fa/confirm", new ConfirmLoginTwoFactorRequest(
                pending.PendingToken, email.LastCode!)),
            client.PostAsJsonAsync("/api/auth/2fa/confirm", new ConfirmLoginTwoFactorRequest(
                pending.PendingToken, email.LastCode!)));
        Assert.Equal(1, confirmations.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, confirmations.Count(response => response.StatusCode == HttpStatusCode.UnprocessableEntity));
    }

    [Fact]
    public async Task Smtp_failure_leaves_two_factor_disabled()
    {
        await using var factory = await CreateFactoryAsync(new ThrowingEmailSender());
        var client = factory.CreateClient();
        var session = await RegisterAsync(client, "+79990000202");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        var response = await client.PostAsJsonAsync("/api/me/2fa/email", new BeginEmailTwoFactorRequest(
            "user@example.test"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();
        Assert.False(await db.UserSecuritySettings.AnyAsync(settings => settings.TwoFactorEnabled));
    }

    [Fact]
    public async Task Deleting_another_users_session_returns_not_found()
    {
        await using var factory = await CreateFactoryAsync(new RecordingEmailSender());
        var client = factory.CreateClient();
        var victim = await RegisterAsync(client, "+79990000203");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", victim.AccessToken);
        var victimSessions = await client.GetFromJsonAsync<List<SessionResponse>>("/api/me/sessions");
        var victimSessionId = Assert.Single(victimSessions!).Id;
        var attacker = await RegisterAsync(client, "+79990000204");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", attacker.AccessToken);

        var response = await client.DeleteAsync($"/api/me/sessions/{victimSessionId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<WebApplicationFactory<Program>> CreateFactoryAsync(IEmailSender emailSender)
    {
        var baseFactory = new MessengerApiFactory(_postgres.GetConnectionString(), "Development", 100, 100);
        var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton(emailSender);
        }));
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MessengerDbContext>().Database.MigrateAsync();
        return factory;
    }

    private static async Task<AuthSessionResponse> RegisterAsync(HttpClient client, string phone)
    {
        client.DefaultRequestHeaders.Authorization = null;
        var challenge = await RequestChallengeAsync(client, phone, "register");
        var response = await client.PostAsJsonAsync("/api/auth/register", new CompleteChallengeRequest(
            challenge.ChallengeId, "111111", "Android"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthSessionResponse>())!;
    }

    private static async Task<ChallengeResponse> RequestChallengeAsync(HttpClient client, string phone, string purpose)
    {
        var response = await client.PostAsJsonAsync("/api/auth/challenges", new ChallengeRequest(phone, "RU", purpose));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChallengeResponse>())!;
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public string? LastCode { get; private set; }
        public Task SendTwoFactorCodeAsync(string recipient, string code, CancellationToken cancellationToken)
        {
            LastCode = code;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingEmailSender : IEmailSender
    {
        public Task SendTwoFactorCodeAsync(string recipient, string code, CancellationToken cancellationToken) =>
            Task.FromException(new IOException("smtp unavailable"));
    }
}
