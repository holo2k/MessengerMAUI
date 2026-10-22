using System.Text;

namespace Messenger.Contracts.Messages;

public static class MessageTextSearch
{
    public static bool Matches(string? body, string? query)
    {
        if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(query)) return false;
        var text = Normalize(body);
        var terms = Normalize(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return terms.Length > 0 && terms.All(term => text.Contains(term, StringComparison.Ordinal));
    }

    private static string Normalize(string value) => value.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Replace('ё', 'е');
}
