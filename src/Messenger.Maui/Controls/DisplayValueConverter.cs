using System.Globalization;
using Messenger.Contracts.Contacts;
using Messenger.Contracts.Messages;
using Messenger.Contracts.Music;

namespace Messenger.Maui.Controls;

public sealed class DisplayValueConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (parameter as string) switch
        {
            "name" => Name(value),
            "initials" => Initials(Name(value)),
            "duration" => TimeSpan.FromMilliseconds(value is long duration ? duration : 0).ToString(@"m\:ss"),
            "message" => value is MessageResponse message ? MessageText(message) : string.Empty,
            "positive" => value is int count && count > 0,
            "inverse" => value is not true,
            "playIcon" => value is true ? "icon_pause.png" : "icon_play.png",
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
        ? "Сообщение удалено"
        : message.Body ?? message.Type switch
        {
            "photo" => "Фотография", "video" => "Видео", "audio" => "Аудиосообщение", _ => "Вложение"
        };

    private static string Join(string first, string last) => $"{first} {last}".Trim();
}
