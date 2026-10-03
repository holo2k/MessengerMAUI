using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Messenger.Api.IntegrationTests.Auth;
using Messenger.Application.Support;
using Messenger.Contracts.Auth;
using Messenger.Contracts.Support;
using Messenger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Messenger.Api.IntegrationTests.Support;

public sealed class SupportAndDeletionEndpointTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").WithDatabase("messenger_support_tests").WithUsername("messenger").WithPassword("messenger-tests-only").Build();
    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Support_is_owner_isolated_and_deletion_revokes_refresh_session()
    {
        await using var factory = new MessengerApiFactory(_postgres.GetConnectionString(), "Development", 100);
        await using (var scope = factory.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<MessengerDbContext>().Database.MigrateAsync();
        var client = factory.CreateClient(); var owner = await Register(client, "+79990000801"); var stranger = await Register(client, "+79990000802");
        Authorize(client, owner); var conversation = await Post<SupportConversationResponse>(client, "/api/support/tickets", new CreateSupportTicketRequest("Тема", "Текст"));
        Authorize(client, stranger); Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/support/tickets/{conversation.Ticket.Id}")).StatusCode);
        Authorize(client, owner); var deletion = await Post<AccountDeletionResponse>(client, "/api/me/deletion/", new { }); Assert.Equal(30, (deletion.ExecuteAt - deletion.RequestedAt).Days);
        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(owner.RefreshToken, "Tests")); Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/me/deletion/")).StatusCode);
    }

    private static async Task<AuthSessionResponse> Register(HttpClient c, string phone) { c.DefaultRequestHeaders.Authorization = null; var ch = await Post<ChallengeResponse>(c, "/api/auth/challenges", new ChallengeRequest(phone, "RU", "register")); return await Post<AuthSessionResponse>(c, "/api/auth/register", new CompleteChallengeRequest(ch.ChallengeId, "111111", "Tests")); }
    private static void Authorize(HttpClient c, AuthSessionResponse s) => c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", s.AccessToken);
    private static async Task<T> Post<T>(HttpClient c, string uri, object body) { using var r = await c.PostAsJsonAsync(uri, body); r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<T>())!; }
}
