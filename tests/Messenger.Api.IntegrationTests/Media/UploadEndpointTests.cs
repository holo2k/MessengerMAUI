using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Messenger.Api.IntegrationTests.Auth;
using Messenger.Contracts.Auth;
using Messenger.Contracts.Media;
using Messenger.Contracts.Chats;
using Messenger.Contracts.Messages;
using Messenger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;

namespace Messenger.Api.IntegrationTests.Media;

public sealed class UploadEndpointTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("messenger_media_tests").WithUsername("messenger").WithPassword("messenger-tests-only").Build();
    private readonly MinioContainer _minio = new MinioBuilder("messenger-minio:RELEASE.2025-10-15T17-29-55Z")
        .WithUsername("messenger-tests").WithPassword("messenger-tests-only").Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _minio.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await _minio.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Real_minio_upload_is_private_checksum_verified_and_authorized_with_short_lived_url()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "+79990000601");
        var outsider = await RegisterAsync(client, "+79990000602");
        var recipient = await RegisterAsync(client, "+79990000604");
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
        Authorize(client, owner);
        var upload = await PostAsync<UploadSessionResponse>(client, "/api/uploads/", new CreateUploadRequest(
            "photo.png", "image/png", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
        using (var put = new HttpRequestMessage(HttpMethod.Put, upload.UploadUrl))
        {
            put.Content = new ByteArrayContent(bytes);
            put.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            (await new HttpClient().SendAsync(put)).EnsureSuccessStatusCode();
        }

        var stored = await PostAsync<StoredObjectResponse>(client, $"/api/uploads/{upload.Id}/complete", new { });
        var authorization = await client.GetFromJsonAsync<DownloadAuthorizationResponse>($"/api/objects/{stored.Id}/download");
        Assert.NotNull(authorization);
        Assert.InRange(authorization.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(6));
        Assert.Equal(bytes, await new HttpClient().GetByteArrayAsync(authorization.Url));

        var chat = await PostAsync<ChatResponse>(client, "/api/chats/direct", new CreateDirectChatRequest(recipient.UserId));
        await PostAsync<MessageResponse>(client, $"/api/chats/{chat.Id}/messages",
            new SendMessageRequest(Guid.NewGuid(), "image", null, [stored.Id]));
        Authorize(client, recipient);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/objects/{stored.Id}/download")).StatusCode);

        Authorize(client, outsider);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/objects/{stored.Id}/download")).StatusCode);
    }

    [Fact]
    public async Task Spoofed_magic_bytes_never_create_a_usable_object()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "+79990000603");
        Authorize(client, owner);
        var bytes = "not a png"u8.ToArray();
        var upload = await PostAsync<UploadSessionResponse>(client, "/api/uploads/", new CreateUploadRequest(
            "photo.png", "image/png", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
        using (var put = new HttpRequestMessage(HttpMethod.Put, upload.UploadUrl))
        {
            put.Content = new ByteArrayContent(bytes);
            put.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            (await new HttpClient().SendAsync(put)).EnsureSuccessStatusCode();
        }

        var response = await client.PostAsJsonAsync($"/api/uploads/{upload.Id}/complete", new { });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<MessengerDbContext>().StoredObjects.ToArrayAsync());
    }

    private async Task<MessengerApiFactory> CreateFactoryAsync()
    {
        var factory = new MessengerApiFactory(_postgres.GetConnectionString(), "Development", 100, 100,
            _minio.GetConnectionString(), _minio.GetAccessKey(), _minio.GetSecretKey());
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MessengerDbContext>().Database.MigrateAsync();
        return factory;
    }

    private static async Task<AuthSessionResponse> RegisterAsync(HttpClient client, string phone)
    {
        client.DefaultRequestHeaders.Authorization = null;
        var challenge = await (await client.PostAsJsonAsync("/api/auth/challenges",
            new ChallengeRequest(phone, "RU", "register"))).Content.ReadFromJsonAsync<ChallengeResponse>();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new CompleteChallengeRequest(challenge!.ChallengeId, "111111", "Tests"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthSessionResponse>())!;
    }

    private static void Authorize(HttpClient client, AuthSessionResponse session) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

    private static async Task<T> PostAsync<T>(HttpClient client, string uri, object request)
    {
        var response = await client.PostAsJsonAsync(uri, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
