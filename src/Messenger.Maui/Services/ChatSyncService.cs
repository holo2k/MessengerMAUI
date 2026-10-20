using Messenger.Contracts.Messages;
using Messenger.Contracts.Chats;
using Messenger.Contracts.Realtime;

namespace Messenger.Maui.Services;

public enum ChatConnectionState
{
    Offline,
    Connecting,
    Connected,
    Reconnecting
}

public interface IChatRealtimeClient
{
    event Func<MessageCreatedEvent, Task>? MessageCreated;
    event Func<ReadPositionChangedEvent, Task>? ReadPositionChanged;
    event Func<Task>? Reconnected;
    event Action<ChatConnectionState>? ConnectionStateChanged;
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task JoinChatAsync(Guid chatId, CancellationToken cancellationToken = default);
}

public interface IChatCursorStore
{
    Task<long> GetAsync(Guid chatId, CancellationToken cancellationToken = default);
    Task SaveAsync(Guid chatId, long cursor, CancellationToken cancellationToken = default);
}

public sealed class ChatSyncService(
    IConversationApi api,
    IChatRealtimeClient realtime,
    IChatCursorStore cursors)
{
    private readonly SemaphoreSlim _reconcileLock = new(1, 1);
    private Guid _chatId;
    private long _cursor;
    private bool _started;
    private int _generation;

    public event Func<MessageResponse, Task>? MessageReceived;
    public event Func<ReadPositionChangedEvent, Task>? ReadPositionChanged;
    public event Func<IReadOnlyList<ChatMemberResponse>, Task>? ReadPositionsLoaded;
    public event Action<ChatConnectionState>? ConnectionStateChanged;

    public async Task StartAsync(Guid chatId, CancellationToken cancellationToken = default)
    {
        if (_started) return;
        _started = true;
        var generation = ++_generation;
        _chatId = chatId;
        try
        {
            var cursor = await cursors.GetAsync(chatId, cancellationToken);
            if (!IsCurrent(generation)) return;
            _cursor = cursor;
            realtime.MessageCreated += OnMessageCreatedAsync;
            realtime.ReadPositionChanged += OnReadPositionChangedAsync;
            realtime.Reconnected += OnReconnectedAsync;
            realtime.ConnectionStateChanged += OnConnectionStateChanged;
            await realtime.ConnectAsync(cancellationToken);
            if (!IsCurrent(generation)) return;
            await realtime.JoinChatAsync(chatId, cancellationToken);
            await ReconcileAsync(generation, cancellationToken);
        }
        catch
        {
            if (IsCurrent(generation)) Stop();
            throw;
        }
    }

    private Task OnMessageCreatedAsync(MessageCreatedEvent message) =>
        _started && message.ChatId == _chatId && message.Sequence > _cursor
            ? ReconcileAsync(_generation)
            : Task.CompletedTask;

    public void Stop()
    {
        _started = false;
        _generation++;
        realtime.MessageCreated -= OnMessageCreatedAsync;
        realtime.ReadPositionChanged -= OnReadPositionChangedAsync;
        realtime.Reconnected -= OnReconnectedAsync;
        realtime.ConnectionStateChanged -= OnConnectionStateChanged;
    }

    private Task OnReconnectedAsync() => _started ? ReconcileAsync(_generation) : Task.CompletedTask;

    private Task OnReadPositionChangedAsync(ReadPositionChangedEvent position) =>
        _started && position.ChatId == _chatId
            ? ReadPositionChanged?.Invoke(position) ?? Task.CompletedTask
            : Task.CompletedTask;

    private void OnConnectionStateChanged(ChatConnectionState state) => ConnectionStateChanged?.Invoke(state);

    private bool IsCurrent(int generation) => _started && generation == _generation;

    private async Task ReconcileAsync(int generation, CancellationToken cancellationToken = default)
    {
        await _reconcileLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsCurrent(generation)) return;
            var chatId = _chatId;
            var messages = await api.GetMessagesAsync(chatId, _cursor, cancellationToken);
            if (!IsCurrent(generation)) return;
            foreach (var message in messages.OrderBy(message => message.Sequence))
            {
                if (!IsCurrent(generation)) return;
                if (message.Sequence <= _cursor)
                {
                    continue;
                }
                if (MessageReceived is not null)
                {
                    await MessageReceived(message);
                }
                if (!IsCurrent(generation)) return;
                _cursor = message.Sequence;
                await cursors.SaveAsync(chatId, _cursor, cancellationToken);
            }
            if (IsCurrent(generation) && ReadPositionsLoaded is not null)
            {
                var members = await api.GetChatMembersAsync(chatId, cancellationToken);
                if (IsCurrent(generation)) await ReadPositionsLoaded(members);
            }
        }
        finally
        {
            _reconcileLock.Release();
        }
    }
}
