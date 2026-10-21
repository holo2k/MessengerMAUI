using Messenger.Contracts.Chats;
using Messenger.Contracts.Realtime;

namespace Messenger.Maui.Services;

public sealed class ChatListSyncService(
    IConversationApi api,
    IChatRealtimeClient realtime)
{
    private bool _started;

    public event Func<ChatResponse, MessageCreatedEvent, Task>? ChatChanged;
    public event Func<Task>? ReloadRequested;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        realtime.MessageCreated += OnMessageCreatedAsync;
        realtime.Reconnected += OnReconnectedAsync;
        try
        {
            try
            {
                await realtime.ConnectAsync(cancellationToken);
            }
            catch (Exception exception) when (ExceptionDisplay.IsUnauthorized(exception))
            {
                // SignalR reads the saved token directly; an API request uses AuthSessionHandler
                // to refresh an expired token before we retry the initial connection once.
                await api.GetChatsAsync(cancellationToken);
                if (_started) await realtime.ConnectAsync(cancellationToken);
            }
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        realtime.MessageCreated -= OnMessageCreatedAsync;
        realtime.Reconnected -= OnReconnectedAsync;
        _started = false;
    }

    private async Task OnMessageCreatedAsync(MessageCreatedEvent message)
    {
        try
        {
            var chat = await api.GetChatAsync(message.ChatId);
            if (ChatChanged is not null)
            {
                await ChatChanged.Invoke(chat, message);
            }
        }
        catch
        {
            // Keep the current list intact. A later reconnect or page load reconciles it.
        }
    }

    private Task OnReconnectedAsync() => ReloadRequested?.Invoke() ?? Task.CompletedTask;
}
