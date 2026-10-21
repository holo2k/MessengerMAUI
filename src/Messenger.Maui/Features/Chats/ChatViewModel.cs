using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Messenger.Contracts.Messages;
using Messenger.Contracts.Media;
using Messenger.Contracts.Chats;
using Messenger.Contracts.Realtime;
using Messenger.Contracts.Music;
using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Chats;

public sealed class ChatViewModel(Guid chatId, IConversationApi api, MediaComposerViewModel? media = null) : ObservableObject
{
    private string _composerText = string.Empty;
    private string _connectionStatus = "Не подключено";
    private readonly Dictionary<Guid, long> _readPositions = [];
    private readonly SemaphoreSlim _readLock = new(1, 1);
    private Guid _currentUserId;
    private long _readSequence, _lastMarkedRead;
    private bool _hasReadSnapshot;

    public ObservableCollection<MessageResponse> Messages { get; } = [];
    public ObservableCollection<MessageResponse> SearchResults { get; } = [];
    public ObservableCollection<MessageResponse> Pins { get; } = [];
    public ObservableCollection<StoredObjectResponse> SharedMedia { get; } = [];
    public Guid ChatId => chatId;
    public MediaComposerViewModel? Media { get; } = media;
    public string ComposerText { get => _composerText; set => SetProperty(ref _composerText, value); }
    public string ConnectionStatus { get => _connectionStatus; private set => SetProperty(ref _connectionStatus, value); }
    public long ReadSequence { get => _readSequence; private set => SetProperty(ref _readSequence, value); }

    public void SetCurrentUser(Guid userId)
    {
        _currentUserId = userId;
        UpdateReadSequence();
    }

    public void ApplyReadPositions(IReadOnlyList<ChatMemberResponse> members)
    {
        _hasReadSnapshot = true;
        var activeIds = members.Select(member => member.UserId).ToHashSet();
        foreach (var userId in _readPositions.Keys.Where(id => !activeIds.Contains(id)).ToArray())
            _readPositions.Remove(userId);
        foreach (var member in members)
            _readPositions[member.UserId] = Math.Max(_readPositions.GetValueOrDefault(member.UserId), member.LastReadSequence);
        _lastMarkedRead = Math.Max(_lastMarkedRead, _readPositions.GetValueOrDefault(_currentUserId));
        UpdateReadSequence();
    }

    public void ApplyReadPosition(ReadPositionChangedEvent position)
    {
        if (position.ChatId != chatId || position.UserId == Guid.Empty || position.Sequence < 0) return;
        _readPositions[position.UserId] = Math.Max(_readPositions.GetValueOrDefault(position.UserId), position.Sequence);
        if (position.UserId == _currentUserId) _lastMarkedRead = Math.Max(_lastMarkedRead, position.Sequence);
        UpdateReadSequence();
    }

    private void UpdateReadSequence() => ReadSequence = !_hasReadSnapshot || _currentUserId == Guid.Empty ? 0 :
        _readPositions.Where(position => position.Key != _currentUserId)
            .Select(position => position.Value).DefaultIfEmpty(0).Min();

    public async Task MarkVisibleReadAsync(long sequence, CancellationToken cancellationToken = default)
    {
        if (_currentUserId == Guid.Empty || sequence <= _lastMarkedRead) return;
        sequence = Math.Min(sequence, Messages.Select(message => message.Sequence).DefaultIfEmpty(0).Max());
        await _readLock.WaitAsync(cancellationToken);
        try
        {
            if (sequence <= _lastMarkedRead) return;
            await api.MarkReadAsync(chatId, sequence, cancellationToken);
            _lastMarkedRead = Math.Max(_lastMarkedRead, sequence);
        }
        finally { _readLock.Release(); }
    }

    public void SetConnectionState(ChatConnectionState state) => ConnectionStatus = state switch
    {
        ChatConnectionState.Connecting => "Подключение…",
        ChatConnectionState.Connected => "В сети",
        ChatConnectionState.Reconnecting => "Переподключение…",
        _ => "Нет соединения"
    };

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var messages = await api.GetMessagesAsync(chatId, 0, cancellationToken);
        Messages.Clear();
        foreach (var message in messages)
        {
            Messages.Add(message);
        }
    }

    public async Task SendAsync(CancellationToken cancellationToken = default)
    {
        if (Media?.Attachments.Any(item => item.State != UploadItemState.Uploaded) == true)
            throw new InvalidOperationException("Дождитесь загрузки вложений или повторите загрузку неудавшихся файлов.");
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
                uploaded[0].Selection.Kind == MediaKind.Photo ? "image" : uploaded[0].Selection.Kind.ToString().ToLowerInvariant(),
                string.IsNullOrWhiteSpace(body) ? null : body,
                uploaded.Select(item => item.StoredObject!.Id).ToArray(),
                cancellationToken);
        if (uploaded.Length > 0 && sent.Attachments is not { Count: > 0 })
            sent = sent with { Attachments = uploaded.Select(item => item.StoredObject!).ToArray() };
        Receive(sent);
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

    public void ApplyMusicTrack(MusicTrackResponse track)
    {
        foreach (var rows in new[] { Messages, SearchResults, Pins })
            for (var index = 0; index < rows.Count; index++)
                if (rows[index].MusicTrack?.Id == track.Id)
                    rows[index] = rows[index] with { MusicTrack = track };
    }
}
