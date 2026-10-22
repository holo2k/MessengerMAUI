using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using Messenger.Contracts.Media;
using Messenger.Contracts.Messages;
using Messenger.Contracts.Music;
using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Chats;

public enum SharedMediaFilter { Media, Audio, Files, Links }

public sealed record SharedMediaItem(Guid MessageId, SharedMediaFilter Kind, StoredObjectResponse? Attachment = null,
    MusicTrackResponse? Track = null, string? Url = null)
{
    public string Key => Attachment is { } file ? $"file:{file.Id}" : Track is { } track ? $"track:{track.Id}" : $"url:{Url}";
    public string Title => Attachment?.FileName ?? Track?.Title ?? Url ?? string.Empty;
    public string Subtitle => Attachment is { } file ? file.Size >= 1024 * 1024 ? $"{file.Size / (1024d * 1024):0.#} МБ" : $"{Math.Max(1, file.Size / 1024d):0.#} КБ"
        : Track is { } track ? $"{track.Artist} · {PlaybackTime.Format(track.DurationMs)}" : "Открыть ссылку";
    public string Icon => Kind switch { SharedMediaFilter.Audio => "tab_music.png", SharedMediaFilter.Links => "icon_link.png", _ => "icon_file.png" };
    public bool IsVideo => Attachment?.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true;
    public bool HasTrack => Track is not null;
}

public sealed partial class SharedMediaViewModel(Guid chatId, IConversationApi api) : ObservableObject
{
    private IReadOnlyList<MessageResponse> _messages = [];
    private readonly HashSet<Guid> _removedIds = [];
    private SharedMediaFilter _filter;
    public ObservableCollection<SharedMediaItem> Items { get; } = [];
    public SharedMediaFilter Filter
    {
        get => _filter;
        set { if (SetProperty(ref _filter, value)) Refresh(); }
    }
    public bool IsMedia => Filter == SharedMediaFilter.Media;
    public string EmptyText => Filter switch
    {
        SharedMediaFilter.Audio => "В чате пока нет аудио",
        SharedMediaFilter.Files => "В чате пока нет файлов",
        SharedMediaFilter.Links => "В чате пока нет ссылок",
        _ => "В чате пока нет фото и видео"
    };

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var messages = chatId == Guid.Empty ? [] : await api.GetMessagesAsync(chatId, 0, ct);
        ct.ThrowIfCancellationRequested();
        _messages = messages;
        Refresh();
    }

    public void RemoveMessage(Guid messageId) { _removedIds.Add(messageId); Refresh(); }

    private void Refresh()
    {
        Items.Clear();
        foreach (var item in _messages.Where(message => message.DeletedAt is null && !_removedIds.Contains(message.Id))
                     .OrderByDescending(message => message.Sequence).SelectMany(Expand).Where(item => item.Kind == Filter).DistinctBy(item => item.Key))
            Items.Add(item);
        OnPropertyChanged(nameof(IsMedia));
        OnPropertyChanged(nameof(EmptyText));
    }

    private static IEnumerable<SharedMediaItem> Expand(MessageResponse message)
    {
        foreach (var file in message.Attachments ?? [])
        {
            var kind = file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) || file.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                ? SharedMediaFilter.Media : file.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ? SharedMediaFilter.Audio : SharedMediaFilter.Files;
            yield return new(message.Id, kind, file);
        }
        if (message.MusicTrack is { } track) yield return new(message.Id, SharedMediaFilter.Audio, Track: track);
        foreach (Match match in LinkPattern().Matches(message.Body ?? string.Empty))
        {
            var link = match.Value.TrimEnd('.', ',', ';', ':', '!', ')', ']', '}');
            if (link.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) link = "https://" + link;
            if (Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
                yield return new(message.Id, SharedMediaFilter.Links, Url: uri.AbsoluteUri);
        }
    }

    [GeneratedRegex(@"\b(?:https?://|www\.)[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex LinkPattern();
}
