namespace Messenger.Domain.Common;

public readonly record struct UtcInstant
{
    private UtcInstant(DateTimeOffset value)
    {
        Value = value;
    }

    public DateTimeOffset Value { get; }

    public static UtcInstant From(DateTimeOffset value) => new(value.ToUniversalTime());
}
