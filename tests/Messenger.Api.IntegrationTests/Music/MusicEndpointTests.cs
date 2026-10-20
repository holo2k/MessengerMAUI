using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Messenger.Api.IntegrationTests.Auth;
using Messenger.Application.Music;
using Messenger.Contracts.Auth;
using Messenger.Contracts.Media;
using Messenger.Contracts.Music;
using Messenger.Contracts.Chats;
using Messenger.Contracts.Messages;
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
    private readonly MinioContainer _minio = new MinioBuilder("messenger-minio:RELEASE.2025-10-15T17-29-55Z")
        .WithUsername("messenger-tests").WithPassword("messenger-tests-only").Build();
    public async Task InitializeAsync() => await Task.WhenAll(_postgres.StartAsync(), _minio.StartAsync());
    public async Task DisposeAsync() { await _minio.DisposeAsync(); await _postgres.DisposeAsync(); }

    [Fact]
    public async Task Track_is_immediately_available_then_block_revokes_new_stream_authorization()
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
        Assert.Equal("available", track.Status);
        Assert.Contains((await client.GetFromJsonAsync<MusicTrackResponse[]>("/api/music/library"))!, x => x.Id == track.Id);
        Assert.Contains((await client.GetFromJsonAsync<MusicTrackResponse[]>("/api/music/search?query=Трек"))!, x => x.Id == track.Id);
        await SuccessAsync(client.PutAsync($"/api/music/library/{track.Id}", null));
        Assert.NotNull(await client.GetFromJsonAsync<DownloadAuthorizationResponse>($"/api/music/tracks/{track.Id}/stream"));

        Authorize(client, claimant);
        await SuccessAsync(client.PostAsJsonAsync($"/api/music/tracks/{track.Id}/claims", new CopyrightClaimRequest("Прошу проверить права")));
        Authorize(client, owner);
        await SuccessAsync(client.PutAsJsonAsync($"/api/admin/music/tracks/{track.Id}", new ModerateTrackRequest("block", "Получена жалоба")));

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/music/tracks/{track.Id}/stream")).StatusCode);
        await using var auditScope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await auditScope.ServiceProvider.GetRequiredService<MessengerDbContext>().ModerationActions.CountAsync());
    }

    [Fact]
    public async Task Covers_are_visible_to_listeners_and_shared_songs_keep_the_current_cover_and_access_checks()
    {
        await using var factory = new MessengerApiFactory(_postgres.GetConnectionString(), "Development", 100, 100,
            _minio.GetConnectionString(), _minio.GetAccessKey(), _minio.GetSecretKey());
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<MessengerDbContext>().Database.MigrateAsync();
        using var ownerClient = factory.CreateClient();
        using var listenerClient = factory.CreateClient();
        using var outsiderClient = factory.CreateClient();
        var owner = await RegisterAsync(ownerClient, "+79990000711");
        var listener = await RegisterAsync(listenerClient, "+79990000712");
        var outsider = await RegisterAsync(outsiderClient, "+79990000713");
        Authorize(ownerClient, owner);
        Authorize(listenerClient, listener);
        Authorize(outsiderClient, outsider);
        factory.Services.GetRequiredService<MusicAdminOptions>().UserIds.Add(owner.UserId);
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
        var cover = await UploadAsync(ownerClient, "cover.png", "image/png", png);
        var replacement = await UploadAsync(ownerClient, "next.png", "image/png", png);
        var privateImage = await UploadAsync(ownerClient, "private.png", "image/png", png);
        var audio = await UploadAsync(ownerClient, "song.mp3", "audio/mpeg", [0x49, 0x44, 0x33, 4, 0, 0, 1]);
        var track = await PostAsync<MusicTrackResponse>(ownerClient, "/api/music/tracks",
            new CreateTrackRequest(audio.Id, "Обложка", "Автор", 1000, cover.Id));
        Assert.Equal(owner.UserId, track.UploaderUserId);
        Assert.Equal(cover.Id, track.CoverObjectId);
        Assert.NotNull(await listenerClient.GetFromJsonAsync<DownloadAuthorizationResponse>($"/api/objects/{cover.Id}/download"));
        Assert.Equal(HttpStatusCode.NotFound, (await listenerClient.GetAsync($"/api/objects/{privateImage.Id}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await listenerClient.PutAsJsonAsync($"/api/music/tracks/{track.Id}/cover",
            new UpdateTrackCoverRequest(replacement.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ownerClient.PutAsJsonAsync($"/api/music/tracks/{track.Id}/cover",
            new UpdateTrackCoverRequest(audio.Id))).StatusCode);

        var chat = await PostAsync<ChatResponse>(ownerClient, "/api/chats/direct", new CreateDirectChatRequest(listener.UserId));
        var request = new SendMessageRequest(Guid.NewGuid(), "music", null, MusicTrackId: track.Id);
        var sent = await PostAsync<MessageResponse>(ownerClient, $"/api/chats/{chat.Id}/messages", request);
        var retry = await PostAsync<MessageResponse>(ownerClient, $"/api/chats/{chat.Id}/messages", request);
        Assert.Equal(sent.Id, retry.Id);
        Assert.Equal(cover.Id, sent.MusicTrack!.CoverObjectId);
        Assert.Equal(HttpStatusCode.NotFound, (await outsiderClient.PostAsJsonAsync($"/api/chats/{chat.Id}/messages", request)).StatusCode);
        await SuccessAsync(ownerClient.PutAsJsonAsync($"/api/music/tracks/{track.Id}/cover", new UpdateTrackCoverRequest(replacement.Id)));
        var history = await listenerClient.GetFromJsonAsync<MessagePageResponse>($"/api/chats/{chat.Id}/messages");
        Assert.Equal(replacement.Id, Assert.Single(history!.Items).MusicTrack!.CoverObjectId);
        Assert.NotNull(await listenerClient.GetFromJsonAsync<DownloadAuthorizationResponse>($"/api/objects/{replacement.Id}/download"));
        Assert.NotNull(await listenerClient.GetFromJsonAsync<DownloadAuthorizationResponse>($"/api/music/tracks/{track.Id}/stream"));
        await PostAsync<MessageResponse>(listenerClient, $"/api/chats/{chat.Id}/messages", request with { ClientMessageId = Guid.NewGuid() });
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();
            Assert.Equal(2, await db.Messages.CountAsync(message => message.ChatId == chat.Id && message.MusicTrackId == track.Id));
        }
        await SuccessAsync(ownerClient.PutAsJsonAsync($"/api/admin/music/tracks/{track.Id}", new ModerateTrackRequest("block", "Недоступно")));
        Assert.Equal(HttpStatusCode.NotFound, (await listenerClient.PostAsJsonAsync($"/api/chats/{chat.Id}/messages",
            request with { ClientMessageId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await listenerClient.GetAsync($"/api/objects/{replacement.Id}/download")).StatusCode);
        history = await listenerClient.GetFromJsonAsync<MessagePageResponse>($"/api/chats/{chat.Id}/messages");
        Assert.All(history!.Items, message => Assert.Null(message.MusicTrack));
    }

    private static async Task<StoredObjectResponse> UploadAsync(HttpClient client, string name, string mime, byte[] bytes)
    {
        var upload = await PostAsync<UploadSessionResponse>(client, "/api/uploads/", new CreateUploadRequest(
            name, mime, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
        using var transfer = new HttpClient();
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(mime);
        using var response = await transfer.PutAsync(upload.UploadUrl, content);
        response.EnsureSuccessStatusCode();
        return await PostAsync<StoredObjectResponse>(client, $"/api/uploads/{upload.Id}/complete", new { });
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
