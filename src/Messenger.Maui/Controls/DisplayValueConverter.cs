using System.Globalization;
using Messenger.Contracts.Contacts;
using Messenger.Contracts.Messages;
using Messenger.Contracts.Music;
using Messenger.Maui.Services;

namespace Messenger.Maui.Controls;

public sealed class DisplayValueConverter : IValueConverter, IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var current = values.Length >= 2 && values[0] is Guid trackId && values[1] is Guid currentId && trackId == currentId;
        return (parameter as string) switch
        {
            "libraryCurrent" or "queueCurrent" => current ? Color.FromArgb("#D7EAFF") : Colors.Transparent,
            "messageMusicPlayIcon" => current && values.Length > 2 && values[2] is true ? "music_pause_dark.png" : "music_play_dark.png",
            _ => current
        };
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (parameter as string) switch
        {
            "name" => Name(value),
            "initials" => Initials(Name(value)),
            "duration" => PlaybackTime.Format(value is long duration ? duration : 0),
            "message" => value is MessageResponse message ? MessageText(message) : string.Empty,
            "positive" => value is int count && count > 0,
            "inverse" => value is not true,
            "playIcon" => value is true ? "icon_pause.png" : "icon_play.png",
            "darkPlayIcon" => value is true ? "music_pause_dark.png" : "music_play_dark.png",
            "savedIcon" => value is true ? "music_saved.png" : "music_add.png",
            "notEmpty" => value is string text && !string.IsNullOrWhiteSpace(text),
            _ => value ?? string.Empty
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public static string Name(object? value) => value switch
    {
        ContactResponse contact => Join(contact.LocalFirstName ?? contact.FirstName, contact.LocalLastName ?? contact.LastName),
        UserSearchResponse user => Join(user.FirstName, user.LastName),
        MusicTrackResponse track => track.Title,
        string text when !string.IsNullOrWhiteSpace(text) => text,
        _ => "?"
    };

    public static string Initials(string name) => string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Take(2).Select(part => StringInfo.GetNextTextElement(part))).ToUpperInvariant();

    public static string MessageText(MessageResponse message) => message.DeletedAt.HasValue
        ? string.Empty
        : message.Type == "music" ? message.MusicTrack is { } track ? $"{track.Title} — {track.Artist}" : "Трек недоступен"
        : !string.IsNullOrWhiteSpace(message.Body) ? message.Body : message.Type switch
        {
            "image" or "photo" => "Фотография", "video" => "Видео", "audio" or "voice" => "Голосовое сообщение",
            "video_note" or "videonote" => "Видеосообщение", _ => "Вложение"
        };

    private static string Join(string first, string last) => $"{first} {last}".Trim();
}
