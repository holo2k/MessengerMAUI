using Messenger.Maui.Services;

namespace Messenger.Maui.Tests.Services;

public sealed class ExceptionDisplayTests
{
    [Fact]
    public void Unauthorized_is_recognized_by_status_even_when_wrapped()
    {
        var unauthorized = new HttpRequestException("Authentication failed", null, System.Net.HttpStatusCode.Unauthorized);
        Assert.True(ExceptionDisplay.IsUnauthorized(unauthorized));
        Assert.True(ExceptionDisplay.IsUnauthorized(new InvalidOperationException("Connection failed", unauthorized)));
    }

    [Fact]
    public void Other_errors_are_not_suppressed_based_on_their_message()
    {
        Assert.False(ExceptionDisplay.IsUnauthorized(new HttpRequestException("401", null, System.Net.HttpStatusCode.Forbidden)));
        Assert.False(ExceptionDisplay.IsUnauthorized(new InvalidOperationException("401")));
    }

    [Fact]
    public void Format_includes_the_outer_error_and_deepest_root_cause()
    {
        var exception = new HttpRequestException(
            "Connection failure",
            new InvalidOperationException(
                "TLS failed",
                new IOException("Certificate chain rejected")));

        var result = ExceptionDisplay.Format(exception);

        Assert.Equal(
            "HttpRequestException: Connection failure" + Environment.NewLine +
            "Причина: IOException: Certificate chain rejected",
            result);
    }

    [Fact]
    public void Format_shows_the_type_and_message_for_an_error_without_inner_exception()
    {
        var result = ExceptionDisplay.Format(new InvalidOperationException("Сначала запросите код."));

        Assert.Equal("InvalidOperationException: Сначала запросите код.", result);
    }
}
