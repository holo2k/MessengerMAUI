using Messenger.Domain.Common;

namespace Messenger.Domain.Tests.Common;

public sealed class PhoneNumberTests
{
    [Fact]
    public void Parse_PreservesCanonicalE164Number()
    {
        var phone = PhoneNumber.Parse("+79991234567", "RU");

        Assert.Equal("+79991234567", phone.E164);
    }

    [Fact]
    public void Parse_NormalizesRussianNationalNumber()
    {
        var phone = PhoneNumber.Parse("8 (999) 123-45-67", "RU");

        Assert.Equal("+79991234567", phone.E164);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-phone")]
    [InlineData("+123")]
    public void Parse_RejectsMalformedNumber(string raw)
    {
        Assert.Throws<FormatException>(() => PhoneNumber.Parse(raw, "RU"));
    }
}
