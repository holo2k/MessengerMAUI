using MailKit.Net.Smtp;
using MailKit.Security;
using Messenger.Application.Users;
using MimeKit;

namespace Messenger.Infrastructure.Email;

public sealed class SmtpOptions
{
    public string Host { get; init; } = "smtp.mail.ru";
    public int Port { get; init; } = 465;
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string FromAddress { get; init; } = string.Empty;
}

public sealed class MailKitEmailSender(SmtpOptions options) : IEmailSender
{
    public async Task SendTwoFactorCodeAsync(
        string recipient,
        string code,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Username) ||
            string.IsNullOrWhiteSpace(options.Password) ||
            string.IsNullOrWhiteSpace(options.FromAddress))
        {
            throw new InvalidOperationException("SMTP credentials and sender address are required.");
        }

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(options.FromAddress));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = "Код входа в Messenger";
        message.Body = new TextPart("plain")
        {
            Text = $"Ваш одноразовый код: {code}. Код действует 5 минут."
        };

        using var client = new SmtpClient();
        await client.ConnectAsync(options.Host, options.Port, SecureSocketOptions.SslOnConnect, cancellationToken);
        await client.AuthenticateAsync(options.Username, options.Password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
