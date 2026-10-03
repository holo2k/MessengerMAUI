using Messenger.Contracts.Realtime;
using Microsoft.AspNetCore.SignalR.Client;

namespace Messenger.Maui.Services;

public sealed class ChatRealtimeClient : IChatRealtimeClient, IMusicRealtimeClient, IAsyncDisposable
{
    private readonly HubConnection _connection;

    public ChatRealtimeClient(ISecureSessionStore sessions)
    {
        var baseAddress = DeviceInfo.Platform == DevicePlatform.Android
            ? "https://10.0.2.2:7106"
            : "https://localhost:7106";
        _connection = new HubConnectionBuilder()
            .WithUrl($"{baseAddress}/hubs/chat", options =>
            {
                options.AccessTokenProvider = async () =>
                    (await sessions.GetAsync())?.Session.AccessToken;
            })
            .WithAutomaticReconnect()
            .Build();
        _connection.On<MessageCreatedEvent>("MessageCreated", message =>
            MessageCreated?.Invoke(message) ?? Task.CompletedTask);
        _connection.On<MusicTrackStatusChangedEvent>("MusicTrackStatusChanged", message =>
            TrackStatusChanged?.Invoke(message) ?? Task.CompletedTask);
        _connection.Reconnecting += _ =>
        {
            ConnectionStateChanged?.Invoke(ChatConnectionState.Reconnecting);
            return Task.CompletedTask;
        };
        _connection.Reconnected += _ =>
        {
            ConnectionStateChanged?.Invoke(ChatConnectionState.Connected);
            return Reconnected?.Invoke() ?? Task.CompletedTask;
        };
        _connection.Closed += _ =>
        {
            ConnectionStateChanged?.Invoke(ChatConnectionState.Offline);
            return Task.CompletedTask;
        };
    }

    public event Func<MessageCreatedEvent, Task>? MessageCreated;
    public event Func<Task>? Reconnected;
    public event Action<ChatConnectionState>? ConnectionStateChanged;
    public event Func<MusicTrackStatusChangedEvent, Task>? TrackStatusChanged;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_connection.State != HubConnectionState.Disconnected)
        {
            return;
        }
        ConnectionStateChanged?.Invoke(ChatConnectionState.Connecting);
        try
        {
            await _connection.StartAsync(cancellationToken);
            ConnectionStateChanged?.Invoke(ChatConnectionState.Connected);
        }
        catch
        {
            ConnectionStateChanged?.Invoke(ChatConnectionState.Offline);
            throw;
        }
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}

public sealed class PreferencesChatCursorStore : IChatCursorStore
{
    public Task<long> GetAsync(Guid chatId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Preferences.Default.Get(Key(chatId), 0L));
    }

    public Task SaveAsync(Guid chatId, long cursor, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Preferences.Default.Set(Key(chatId), cursor);
        return Task.CompletedTask;
    }

    private static string Key(Guid chatId) => $"messenger.chat.cursor.{chatId:N}";
}
