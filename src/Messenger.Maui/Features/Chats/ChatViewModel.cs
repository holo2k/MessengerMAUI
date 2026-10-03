using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Messenger.Contracts.Messages;
using Messenger.Contracts.Media;
using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Chats;

public sealed class ChatViewModel(Guid chatId, IConversationApi api, MediaComposerViewModel? media = null) : ObservableObject
{
    private string _composerText = string.Empty;
    private string _connectionStatus = "Не подключено";

    public ObservableCollection<MessageResponse> Messages { get; } = [];
    public ObservableCollection<MessageResponse> SearchResults { get; } = [];
    public ObservableCollection<MessageResponse> Pins { get; } = [];
    public ObservableCollection<StoredObjectResponse> SharedMedia { get; } = [];
    public Guid ChatId => chatId;
    public MediaComposerViewModel? Media { get; } = media;
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
        var uploaded = Media?.Attachments
            .Where(item => item.State == UploadItemState.Uploaded && item.StoredObject is not null)
            .ToArray() ?? [];
        if (string.IsNullOrWhiteSpace(body) && uploaded.Length == 0)
        {
            return;
        }
        var sent = uploaded.Length == 0
            ? await api.SendAsync(chatId, Guid.NewGuid(), body, cancellationToken)
            : await api.SendWithAttachmentsAsync(
                chatId,
                Guid.NewGuid(),
                uploaded[0].Selection.Kind.ToString().ToLowerInvariant(),
                string.IsNullOrWhiteSpace(body) ? null : body,
                uploaded.Select(item => item.StoredObject!.Id).ToArray(),
                cancellationToken);
        Messages.Add(sent);
        ComposerText = string.Empty;
        Media?.Attachments.Clear();
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

    public async Task LoadMediaAsync(CancellationToken cancellationToken = default)
    {
        SharedMedia.Clear();
        foreach (var value in await api.GetChatMediaAsync(chatId, cancellationToken))
        {
            SharedMedia.Add(value);
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
