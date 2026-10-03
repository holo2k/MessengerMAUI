using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Messenger.Contracts.Messages;
using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Chats;

public sealed class ChatViewModel(Guid chatId, IConversationApi api) : ObservableObject
{
    private string _composerText = string.Empty;
    private string _connectionStatus = "Не подключено";

    public ObservableCollection<MessageResponse> Messages { get; } = [];
    public ObservableCollection<MessageResponse> SearchResults { get; } = [];
    public ObservableCollection<MessageResponse> Pins { get; } = [];
    public Guid ChatId => chatId;
    public string ComposerText { get => _composerText; set => SetProperty(ref _composerText, value); }
    public string ConnectionStatus { get => _connectionStatus; private set => SetProperty(ref _connectionStatus, value); }

    public void SetConnectionState(ChatConnectionState state) => ConnectionStatus = state switch
    {
        ChatConnectionState.Connecting => "Подключение…",
        ChatConnectionState.Connected => "В сети",
        ChatConnectionState.Reconnecting => "Переподключение…",
        _ => "Нет соединения"
    };

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Messages.Clear();
        foreach (var message in await api.GetMessagesAsync(chatId, 0, cancellationToken))
        {
            Messages.Add(message);
        }
    }

    public async Task SendAsync(CancellationToken cancellationToken = default)
    {
        var body = ComposerText;
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }
        var sent = await api.SendAsync(chatId, Guid.NewGuid(), body, cancellationToken);
        Messages.Add(sent);
        ComposerText = string.Empty;
    }

    public async Task SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        SearchResults.Clear();
        foreach (var message in await api.SearchAsync(chatId, query, cancellationToken))
        {
            SearchResults.Add(message);
        }
    }

    public async Task LoadPinsAsync(CancellationToken cancellationToken = default)
    {
        Pins.Clear();
        foreach (var message in await api.GetPinsAsync(chatId, cancellationToken))
        {
            Pins.Add(message);
        }
    }

    public void Receive(MessageResponse message)
    {
        if (Messages.All(existing => existing.Id != message.Id))
        {
            Messages.Add(message);
        }
    }
}
