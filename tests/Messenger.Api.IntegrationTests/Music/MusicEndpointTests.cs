using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Messenger.Api.IntegrationTests.Auth;
using Messenger.Application.Music;
using Messenger.Contracts.Auth;
using Messenger.Contracts.Media;
using Messenger.Contracts.Music;
using Messenger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;

namespace Messenger.Api.IntegrationTests.Music;

public sealed class MusicEndpointTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("messenger_music_tests").WithUsername("messenger").WithPassword("messenger-tests-only").Build();
    private readonly MinioContainer _minio = new MinioBuilder("minio/minio@sha256:14cea493d9a34af32f524e538b8346cf79f3321eff8e708c1e2960462bd8936e")
        .WithUsername("messenger-tests").WithPassword("messenger-tests-only").Build();
    public async Task InitializeAsync() => await Task.WhenAll(_postgres.StartAsync(), _minio.StartAsync());
    public async Task DisposeAsync() { await _minio.DisposeAsync(); await _postgres.DisposeAsync(); }

    [Fact]
    public async Task Track_requires_declaration_and_admin_then_block_revokes_new_stream_authorization()
    {
        await using var factory = new MessengerApiFactory(_postgres.GetConnectionString(), "Development", 100, 100,
            _minio.GetConnectionString(), _minio.GetAccessKey(), _minio.GetSecretKey());
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<MessengerDbContext>().Database.MigrateAsync();
        var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "+79990000701");
        var claimant = await RegisterAsync(client, "+79990000702");
        factory.Services.GetRequiredService<MusicAdminOptions>().UserIds.Add(owner.UserId);
        Authorize(client, owner);
        var bytes = new byte[] { 0x49, 0x44, 0x33, 4, 0, 0, 1 };
        var upload = await PostAsync<UploadSessionResponse>(client, "/api/uploads/", new CreateUploadRequest(
            "track.mp3", "audio/mpeg", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
        using (var put = new HttpRequestMessage(HttpMethod.Put, upload.UploadUrl) { Content = new ByteArrayContent(bytes) })
        { put.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg"); (await new HttpClient().SendAsync(put)).EnsureSuccessStatusCode(); }
        var stored = await PostAsync<StoredObjectResponse>(client, $"/api/uploads/{upload.Id}/complete", new { });
        var track = await PostAsync<MusicTrackResponse>(client, "/api/music/tracks", new CreateTrackRequest(stored.Id, "Трек", "Автор", 123000, null));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/music/tracks/{track.Id}/stream")).StatusCode);
        await SuccessAsync(client.PostAsJsonAsync($"/api/music/tracks/{track.Id}/declaration", new RightsDeclarationRequest("Подтверждаю наличие прав")));
        await SuccessAsync(client.PutAsJsonAsync($"/api/admin/music/tracks/{track.Id}", new ModerateTrackRequest("approve", "Проверено")));
        await SuccessAsync(client.PutAsync($"/api/music/library/{track.Id}", null));
        Assert.NotNull(await client.GetFromJsonAsync<DownloadAuthorizationResponse>($"/api/music/tracks/{track.Id}/stream"));

        Authorize(client, claimant);
        await SuccessAsync(client.PostAsJsonAsync($"/api/music/tracks/{track.Id}/claims", new CopyrightClaimRequest("Прошу проверить права")));
        Authorize(client, owner);
        await SuccessAsync(client.PutAsJsonAsync($"/api/admin/music/tracks/{track.Id}", new ModerateTrackRequest("block", "Получена жалоба")));

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/music/tracks/{track.Id}/stream")).StatusCode);
        await using var auditScope = factory.Services.CreateAsyncScope();
        Assert.Equal(2, await auditScope.ServiceProvider.GetRequiredService<MessengerDbContext>().ModerationActions.CountAsync());
    }

    private static async Task SuccessAsync(Task<HttpResponseMessage> request)
    {
        using var response = await request;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
    private static async Task<AuthSessionResponse> RegisterAsync(HttpClient client, string phone)
    {
        client.DefaultRequestHeaders.Authorization = null;
        var c = await (await client.PostAsJsonAsync("/api/auth/challenges", new ChallengeRequest(phone, "RU", "register"))).Content.ReadFromJsonAsync<ChallengeResponse>();
        var r = await client.PostAsJsonAsync("/api/auth/register", new CompleteChallengeRequest(c!.ChallengeId, "111111", "Tests")); r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<AuthSessionResponse>())!;
    }
    private static void Authorize(HttpClient c, AuthSessionResponse s) => c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", s.AccessToken);
    private static async Task<T> PostAsync<T>(HttpClient c, string uri, object body) { var r = await c.PostAsJsonAsync(uri, body); r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<T>())!; }
}
