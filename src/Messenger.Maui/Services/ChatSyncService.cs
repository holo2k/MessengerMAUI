using Messenger.Contracts.Messages;
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
    event Func<Task>? Reconnected;
    event Action<ChatConnectionState>? ConnectionStateChanged;
    Task ConnectAsync(CancellationToken cancellationToken = default);
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

    public event Func<MessageResponse, Task>? MessageReceived;
    public event Action<ChatConnectionState>? ConnectionStateChanged;

    public async Task StartAsync(Guid chatId, CancellationToken cancellationToken = default)
    {
        _chatId = chatId;
        _cursor = await cursors.GetAsync(chatId, cancellationToken);
        realtime.MessageCreated += OnMessageCreatedAsync;
        realtime.Reconnected += OnReconnectedAsync;
        realtime.ConnectionStateChanged += OnConnectionStateChanged;
        await ReconcileAsync(cancellationToken);
        await realtime.ConnectAsync(cancellationToken);
    }

    private Task OnMessageCreatedAsync(MessageCreatedEvent message) =>
        message.ChatId == _chatId && message.Sequence > _cursor
            ? ReconcileAsync()
            : Task.CompletedTask;

    private Task OnReconnectedAsync() => ReconcileAsync();

    private void OnConnectionStateChanged(ChatConnectionState state) => ConnectionStateChanged?.Invoke(state);

    private async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        await _reconcileLock.WaitAsync(cancellationToken);
        try
        {
            var messages = await api.GetMessagesAsync(_chatId, _cursor, cancellationToken);
            foreach (var message in messages.OrderBy(message => message.Sequence))
            {
                if (message.Sequence <= _cursor)
                {
                    continue;
                }
                if (MessageReceived is not null)
                {
                    await MessageReceived(message);
                }
                _cursor = message.Sequence;
                await cursors.SaveAsync(_chatId, _cursor, cancellationToken);
            }
        }
        finally
        {
            _reconcileLock.Release();
        }
    }
}
