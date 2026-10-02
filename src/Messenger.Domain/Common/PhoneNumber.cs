namespace Messenger.Domain.Common;

public sealed record PhoneNumber
{
    private PhoneNumber(string e164)
    {
        E164 = e164;
    }

    public string E164 { get; }

    public static PhoneNumber Parse(string raw, string defaultRegion)
    {
        if (!string.IsNullOrWhiteSpace(raw) &&
            raw.StartsWith('+') &&
            raw.Length is >= 9 and <= 16 &&
            raw.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0)
        {
            return new PhoneNumber(raw);
        }

        if (!string.IsNullOrWhiteSpace(raw) &&
            string.Equals(defaultRegion, "RU", StringComparison.OrdinalIgnoreCase))
        {
            var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());
            if (digits.Length == 11 && digits[0] == '8')
            {
                return new PhoneNumber($"+7{digits[1..]}");
            }
        }

        throw new FormatException("Phone number is not valid for the selected country.");
    }
}
