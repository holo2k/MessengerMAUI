using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Messenger.Application.Music;
using Messenger.Contracts.Auth;
using Messenger.Contracts.Chats;
using Messenger.Contracts.Media;
using Messenger.Contracts.Messages;
using Messenger.Contracts.Music;
using Messenger.Contracts.Support;
using Messenger.Contracts.Users;
using Messenger.Contracts.Realtime;
using Messenger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR.Client;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;

namespace Messenger.EndToEndTests;

public sealed class MessengerJourneyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").WithDatabase("messenger_e2e").WithUsername("messenger").WithPassword("messenger-tests-only").Build();
    private readonly MinioContainer _minio = new MinioBuilder("messenger-minio:RELEASE.2025-10-15T17-29-55Z").WithUsername("messenger-tests").WithPassword("messenger-tests-only").Build();
    public async Task InitializeAsync() => await Task.WhenAll(_postgres.StartAsync(), _minio.StartAsync());
    public async Task DisposeAsync() { await _minio.DisposeAsync(); await _postgres.DisposeAsync(); }

    [Fact]
    public async Task Complete_mobile_user_journey_works_across_the_real_persistence_stack()
    {
        await using var smtp = new TestSmtpServer();
        await using var factory = new JourneyFactory(_postgres.GetConnectionString(), _minio, smtp.Port);
        await using (var scope = factory.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<MessengerDbContext>().Database.MigrateAsync();
        var client = factory.CreateClient(); var alice = await Register(client, "+79990001001"); var bob = await Register(client, "+79990001002"); var carol = await Register(client, "+79990001003");
        factory.Services.GetRequiredService<MusicAdminOptions>().UserIds.Add(alice.UserId);

        Authorize(client, alice); var direct = await Post<ChatResponse>(client, "/api/chats/direct", new CreateDirectChatRequest(bob.UserId));
        var directMessage = await Post<MessageResponse>(client, $"/api/chats/{direct.Id}/messages", new SendMessageRequest(Guid.NewGuid(), "text", "Личное сообщение"));
        var group = await Post<ChatResponse>(client, "/api/chats/groups", new CreateGroupChatRequest("Команда", [bob.UserId, carol.UserId]));
        var sent = await Post<MessageResponse>(client, $"/api/chats/{group.Id}/messages", new SendMessageRequest(Guid.NewGuid(), "text", "Первое сообщение"));
        await Success(client.PutAsJsonAsync($"/api/chats/{group.Id}/read", new ReadChatRequest(sent.Sequence)));
        await Success(client.PutAsJsonAsync($"/api/chats/{direct.Id}/archive", new ChatFlagRequest(true)));
        var folder = await Post<ChatFolderResponse>(client, "/api/chat-folders/", new CreateChatFolderRequest("Работа"));
        await Success(client.PutAsJsonAsync($"/api/chat-folders/{folder.Id}/chats", new SetChatFolderChatsRequest([group.Id])));

        Authorize(client, bob); var directPage = await client.GetFromJsonAsync<MessagePageResponse>($"/api/chats/{direct.Id}/messages?cursor=0"); Assert.Equal(directMessage.Id, Assert.Single(directPage!.Items).Id);
        await using var reconnecting = Connection(factory, bob.AccessToken); await reconnecting.StartAsync(); await reconnecting.StopAsync();
        Authorize(client, alice); var missed = await Post<MessageResponse>(client, $"/api/chats/{group.Id}/messages", new SendMessageRequest(Guid.NewGuid(), "text", "Пропущено при разрыве"));
        await reconnecting.StartAsync();
        Authorize(client, bob); var reconciled = await client.GetFromJsonAsync<MessagePageResponse>($"/api/chats/{group.Id}/messages?cursor={sent.Sequence}"); Assert.Equal(missed.Id, Assert.Single(reconciled!.Items).Id);

        Authorize(client, alice); var audio = new byte[] { 0x49, 0x44, 0x33, 4, 0, 0, 1 };
        var stored = await Upload(client, audio);
        var mediaMessage = await Post<MessageResponse>(client, $"/api/chats/{group.Id}/messages", new SendMessageRequest(Guid.NewGuid(), "audio", null, [stored.Id])); Assert.Equal("audio", mediaMessage.Type);
        var track = await Post<MusicTrackResponse>(client, "/api/music/tracks", new CreateTrackRequest(stored.Id, "E2E трек", "Автор", 1000, null));
        await Success(client.PostAsJsonAsync($"/api/music/tracks/{track.Id}/declaration", new RightsDeclarationRequest("Подтверждаю права")));
        await Success(client.PutAsJsonAsync($"/api/admin/music/tracks/{track.Id}", new ModerateTrackRequest("approve", "ok")));
        Assert.NotNull(await client.GetFromJsonAsync<DownloadAuthorizationResponse>($"/api/music/tracks/{track.Id}/stream"));
        await Success(client.PutAsJsonAsync($"/api/admin/music/tracks/{track.Id}", new ModerateTrackRequest("block", "жалоба")));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/music/tracks/{track.Id}/stream")).StatusCode);

        var setup = await Post<EmailChallengeResponse>(client, "/api/me/2fa/email", new BeginEmailTwoFactorRequest("alice@example.test"));
        await Success(client.PostAsJsonAsync("/api/me/2fa/email/confirm", new ConfirmEmailTwoFactorRequest(setup.ChallengeId, smtp.LastCode!)));
        var ticket = await Post<SupportConversationResponse>(client, "/api/support/tickets", new CreateSupportTicketRequest("E2E", "Нужна помощь")); Assert.Equal("open", ticket.Ticket.Status);
        var deletion = await Post<AccountDeletionResponse>(client, "/api/me/deletion/", new { }); Assert.Equal(30, (deletion.ExecuteAt - deletion.RequestedAt).Days);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/me/deletion/")).StatusCode);

        client.DefaultRequestHeaders.Authorization = null; var loginChallenge = await Post<ChallengeResponse>(client, "/api/auth/challenges", new ChallengeRequest("+79990001001", "RU", "login"));
        var login = await client.PostAsJsonAsync("/api/auth/login", new CompleteChallengeRequest(loginChallenge.ChallengeId, "111111", "iOS")); Assert.Equal(HttpStatusCode.Accepted, login.StatusCode);
        var pending = (await login.Content.ReadFromJsonAsync<PendingTwoFactorResponse>())!;
        var confirmed = await client.PostAsJsonAsync("/api/auth/2fa/confirm", new ConfirmLoginTwoFactorRequest(pending.PendingToken, smtp.LastCode!)); confirmed.EnsureSuccessStatusCode();
        var refreshedLogin = (await confirmed.Content.ReadFromJsonAsync<AuthSessionResponse>())!;
        var rotated = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(refreshedLogin.RefreshToken, "iOS")); rotated.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(refreshedLogin.RefreshToken, "iOS"))).StatusCode);
    }

    private static async Task<StoredObjectResponse> Upload(HttpClient client, byte[] bytes)
    {
        var upload = await Post<UploadSessionResponse>(client, "/api/uploads/", new CreateUploadRequest("track.mp3", "audio/mpeg", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
        using var put = new HttpRequestMessage(HttpMethod.Put, upload.UploadUrl) { Content = new ByteArrayContent(bytes) }; put.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg"); (await new HttpClient().SendAsync(put)).EnsureSuccessStatusCode();
        return await Post<StoredObjectResponse>(client, $"/api/uploads/{upload.Id}/complete", new { });
    }
    private static async Task<AuthSessionResponse> Register(HttpClient c, string phone) { c.DefaultRequestHeaders.Authorization = null; var challenge = await Post<ChallengeResponse>(c, "/api/auth/challenges", new ChallengeRequest(phone, "RU", "register")); return await Post<AuthSessionResponse>(c, "/api/auth/register", new CompleteChallengeRequest(challenge.ChallengeId, "111111", "Android")); }
    private static void Authorize(HttpClient c, AuthSessionResponse session) => c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
    private static HubConnection Connection(JourneyFactory factory, string token) => new HubConnectionBuilder().WithUrl("http://localhost/hubs/chat", options => { options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler(); options.AccessTokenProvider = () => Task.FromResult<string?>(token); }).Build();
    private static async Task<T> Post<T>(HttpClient c, string uri, object body) { using var response = await c.PostAsJsonAsync(uri, body); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<T>())!; }
    private static async Task Success(Task<HttpResponseMessage> operation) { using var response = await operation; response.EnsureSuccessStatusCode(); }

    private sealed class JourneyFactory(string connectionString, MinioContainer minio, int smtpPort) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Messenger"] = connectionString, ["Encryption:ActiveKeyVersion"] = "1", ["Encryption:Keys:1"] = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray()),
                ["BlindIndex:Key"] = Convert.ToBase64String(Enumerable.Repeat((byte)2, 32).ToArray()), ["ChallengeHash:Key"] = Convert.ToBase64String(Enumerable.Repeat((byte)3, 32).ToArray()),
                ["Jwt:Issuer"] = "e2e", ["Jwt:Audience"] = "e2e", ["Jwt:SigningKey"] = Convert.ToBase64String(Enumerable.Repeat((byte)4, 32).ToArray()), ["Sms:Provider"] = "Development",
                ["Minio:Endpoint"] = minio.GetConnectionString(), ["Minio:AccessKey"] = minio.GetAccessKey(), ["Minio:SecretKey"] = minio.GetSecretKey(), ["Minio:Bucket"] = $"e2e-{Guid.NewGuid():N}",
                ["Smtp:Host"] = "127.0.0.1", ["Smtp:Port"] = smtpPort.ToString(), ["Smtp:Username"] = "e2e", ["Smtp:Password"] = "e2e-only", ["Smtp:FromAddress"] = "e2e@example.test", ["Smtp:UseSsl"] = "false"
            }));
        }
    }

    private sealed class TestSmtpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(System.Net.IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        public TestSmtpServer() { _listener.Start(); Port = ((System.Net.IPEndPoint)_listener.LocalEndpoint).Port; _loop = AcceptAsync(); }
        public int Port { get; }
        public string? LastCode { get; private set; }
        private async Task AcceptAsync()
        {
            try { while (!_stop.IsCancellationRequested) _ = HandleAsync(await _listener.AcceptTcpClientAsync(_stop.Token)); }
            catch (OperationCanceledException) { }
        }
        private async Task HandleAsync(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.ASCII, false, leaveOpen: true))
            using (var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true })
            {
                await writer.WriteLineAsync("220 localhost E2E SMTP");
                while (await reader.ReadLineAsync() is { } line)
                {
                    if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase)) { await writer.WriteLineAsync("250-localhost"); await writer.WriteLineAsync("250 AUTH PLAIN"); }
                    else if (line.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase)) await writer.WriteLineAsync("235 authenticated");
                    else if (line.StartsWith("MAIL", StringComparison.OrdinalIgnoreCase) || line.StartsWith("RCPT", StringComparison.OrdinalIgnoreCase) || line.StartsWith("RSET", StringComparison.OrdinalIgnoreCase)) await writer.WriteLineAsync("250 ok");
                    else if (line.Equals("DATA", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("354 end with dot"); var data = new StringBuilder(); string? row;
                        while ((row = await reader.ReadLineAsync()) is not null && row != ".") data.AppendLine(row.StartsWith("..", StringComparison.Ordinal) ? row[1..] : row);
                        using var message = MimeKit.MimeMessage.Load(new MemoryStream(Encoding.ASCII.GetBytes(data.ToString())));
                        LastCode = Regex.Match(message.TextBody ?? string.Empty, @"\b\d{6}\b").Value;
                        await writer.WriteLineAsync("250 queued");
                    }
                    else if (line.Equals("QUIT", StringComparison.OrdinalIgnoreCase)) { await writer.WriteLineAsync("221 bye"); break; }
                    else await writer.WriteLineAsync("250 ok");
                }
            }
        }
        public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Stop(); try { await _loop; } catch (ObjectDisposedException) { } _stop.Dispose(); }
    }
}
