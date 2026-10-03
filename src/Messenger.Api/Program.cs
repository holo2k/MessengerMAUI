using System.Text.Json;
using System.Threading.RateLimiting;
using Messenger.Api.Configuration;
using Messenger.Api.Endpoints;
using Messenger.Application.Identity;
using Messenger.Application.Security;
using Messenger.Application.Users;
using Messenger.Application.Contacts;
using Messenger.Application.Chats;
using Messenger.Domain.Common;
using Messenger.Infrastructure;
using Messenger.Infrastructure.Identity;
using Messenger.Infrastructure.Email;
using Messenger.Infrastructure.Persistence;
using Messenger.Infrastructure.Security;
using Messenger.Infrastructure.Users;
using Messenger.Infrastructure.Social;
using Messenger.Application.Media;
using Messenger.Infrastructure.Media;
using Messenger.Api.Background;
using Messenger.Application.Music;
using Messenger.Infrastructure.Music;
using Messenger.Domain.Music;
using Messenger.Domain.Chats;
using Messenger.Domain.Support;
using Messenger.Domain.Accounts;
using Messenger.Application.Support;
using Messenger.Application.Accounts;
using Messenger.Infrastructure.Support;
using Messenger.Infrastructure.Accounts;
using Messenger.Api.Hubs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<MessengerDbContext>((serviceProvider, options) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("Messenger")
        ?? throw new InvalidOperationException("ConnectionStrings:Messenger is required.");
    options.UseNpgsql(connectionString);
});
builder.Services.AddScoped<IIdentityStore, EfIdentityStore>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<EfUserSettingsStore>();
builder.Services.AddScoped<IUserSettingsStore>(serviceProvider => serviceProvider.GetRequiredService<EfUserSettingsStore>());
builder.Services.AddScoped<ITwoFactorStore>(serviceProvider => serviceProvider.GetRequiredService<EfUserSettingsStore>());
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<ITwoFactorService, TwoFactorService>();
builder.Services.AddScoped<EfSocialStore>();
builder.Services.AddScoped<IContactStore>(serviceProvider => serviceProvider.GetRequiredService<EfSocialStore>());
builder.Services.AddScoped<IChatStore>(serviceProvider => serviceProvider.GetRequiredService<EfSocialStore>());
builder.Services.AddScoped<IChatFolderStore>(serviceProvider => serviceProvider.GetRequiredService<EfSocialStore>());
builder.Services.AddScoped<IContactService, ContactService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IChatFolderService, ChatFolderService>();
builder.Services.AddScoped<IMessageStore, EfMessageStore>();
builder.Services.AddScoped<MessageService>();
builder.Services.AddScoped<IMessageService>(serviceProvider => serviceProvider.GetRequiredService<MessageService>());
builder.Services.AddScoped<IMessageSearchService>(serviceProvider => serviceProvider.GetRequiredService<MessageService>());
builder.Services.AddScoped<IUploadStore, EfUploadStore>();
builder.Services.AddScoped<IUploadService, UploadService>();
builder.Services.AddSingleton<IFileInspector, FileSignatureInspector>();
builder.Services.AddSingleton<IMalwareScanner>(new DevelopmentMalwareScanner(builder.Environment.IsDevelopment()));
builder.Services.AddSingleton<IObjectStore>(serviceProvider => new MinioObjectStore(
    serviceProvider.GetRequiredService<IConfiguration>().GetSection("Minio").Get<MinioOptions>()
    ?? throw new InvalidOperationException("Minio configuration is required.")));
builder.Services.AddHostedService<UploadCleanupWorker>();
builder.Services.AddScoped<IMusicStore, EfMusicStore>();
builder.Services.AddScoped<IMusicService, MusicService>();
builder.Services.AddScoped<IMusicModerationService, MusicModerationService>();
builder.Services.AddSingleton(serviceProvider =>
    serviceProvider.GetRequiredService<IConfiguration>().GetSection("MusicAdmin").Get<MusicAdminOptions>() ?? new MusicAdminOptions());
builder.Services.AddSignalR();
builder.Services.AddSingleton<IChatEventPublisher, SignalRChatEventPublisher>();
builder.Services.AddSingleton<IFieldCipher>(serviceProvider =>
    new AesGcmFieldCipher(serviceProvider.GetRequiredService<IConfiguration>()
        .GetSection("Encryption").Get<FieldCipherOptions>()
        ?? throw new InvalidOperationException("Encryption configuration is required.")));
builder.Services.AddSingleton<IBlindIndex>(serviceProvider =>
    new HmacBlindIndex(serviceProvider.GetRequiredService<IConfiguration>()
        .GetSection("BlindIndex").Get<BlindIndexOptions>()
        ?? throw new InvalidOperationException("BlindIndex configuration is required.")));
builder.Services.AddSingleton<IChallengeCodeHasher>(serviceProvider =>
    new ChallengeCodeHasher(serviceProvider.GetRequiredService<IConfiguration>()
        .GetSection("ChallengeHash").Get<ChallengeHashOptions>()
        ?? throw new InvalidOperationException("ChallengeHash configuration is required.")));
builder.Services.AddSingleton<ISmsSender, DevelopmentSmsSender>();
builder.Services.AddSingleton<IAccessTokenIssuer>(serviceProvider =>
    new JwtTokenIssuer(serviceProvider.GetRequiredService<IConfiguration>()
        .GetSection("Jwt").Get<JwtOptions>()
        ?? throw new InvalidOperationException("JWT configuration is required.")));
builder.Services.AddSingleton<IRefreshTokenGenerator, RefreshTokenGenerator>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IOneTimeCodeGenerator, OneTimeCodeGenerator>();
builder.Services.AddSingleton<IPendingLoginTokenGenerator, PendingLoginTokenGenerator>();
builder.Services.AddSingleton<IEmailSender>(serviceProvider =>
    new MailKitEmailSender(serviceProvider.GetRequiredService<IConfiguration>()
        .GetSection("Smtp").Get<SmtpOptions>() ?? new SmtpOptions()));
builder.Services.AddSingleton(serviceProvider =>
    serviceProvider.GetRequiredService<IConfiguration>().GetSection("Auth").Get<AuthOptions>()
    ?? new AuthOptions());
builder.Services.AddSingleton(serviceProvider =>
    serviceProvider.GetRequiredService<IConfiguration>().GetSection("TwoFactor").Get<TwoFactorOptions>()
    ?? new TwoFactorOptions());
builder.Services.AddScoped<ISupportStore, EfSupportStore>();
builder.Services.AddScoped<ISupportService, SupportService>();
builder.Services.AddScoped<IAccountDeletionStore, EfAccountDeletionStore>();
builder.Services.AddScoped<IAccountDeletionService, AccountDeletionService>();
builder.Services.AddSingleton(serviceProvider =>
    serviceProvider.GetRequiredService<IConfiguration>().GetSection("SupportAdmin").Get<SupportAdminOptions>()
    ?? new SupportAdminOptions());
builder.Services.AddHostedService<AccountDeletionWorker>();
builder.Services.AddHostedService<SmsProviderStartupValidator>();
builder.Services.AddProblemDetails();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
            ?? throw new InvalidOperationException("JWT configuration is required.");
        var jwtSigningKey = Convert.FromBase64String(jwtOptions.SigningKey);
        options.MapInboundClaims = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(jwtSigningKey),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth-challenge", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimits:ChallengePermitLimit", 5),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("auth-code", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimits:CodePermitLimit", 10),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, code) = exception switch
    {
        AuthException { Code: AuthErrorCode.InvalidRefreshToken } =>
            (StatusCodes.Status401Unauthorized, "invalid_refresh_token"),
        AuthException { Code: AuthErrorCode.RefreshTokenReused } =>
            (StatusCodes.Status409Conflict, "refresh_token_reused"),
        AuthException { Code: AuthErrorCode.PhoneAlreadyRegistered } =>
            (StatusCodes.Status409Conflict, "phone_already_registered"),
        AuthException { Code: AuthErrorCode.ChallengeNotFound } =>
            (StatusCodes.Status404NotFound, "challenge_not_found"),
        AuthException authException =>
            (StatusCodes.Status422UnprocessableEntity, ToSnakeCase(authException.Code.ToString())),
        UserSettingsException { Code: UserSettingsError.UsernameTaken } =>
            (StatusCodes.Status409Conflict, "username_taken"),
        UserSettingsException { Code: UserSettingsError.UserNotFound } =>
            (StatusCodes.Status404NotFound, "user_not_found"),
        UserSettingsException userSettingsException =>
            (StatusCodes.Status422UnprocessableEntity, ToSnakeCase(userSettingsException.Code.ToString())),
        ContactRuleException { Code: ContactRuleError.ContactNotFound or ContactRuleError.UserNotFound } =>
            (StatusCodes.Status404NotFound, "contact_not_found"),
        ContactRuleException { Code: ContactRuleError.Duplicate } =>
            (StatusCodes.Status409Conflict, "duplicate_contact"),
        ContactRuleException contactRuleException =>
            (StatusCodes.Status422UnprocessableEntity, ToSnakeCase(contactRuleException.Code.ToString())),
        ChatRuleException { Code: ChatRuleError.NotFound } =>
            (StatusCodes.Status404NotFound, "chat_not_found"),
        ChatRuleException { Code: ChatRuleError.Forbidden } =>
            (StatusCodes.Status403Forbidden, "forbidden"),
        ChatRuleException chatRuleException =>
            (StatusCodes.Status422UnprocessableEntity, ToSnakeCase(chatRuleException.Code.ToString())),
        MessageRuleException { Code: MessageRuleError.NotFound } =>
            (StatusCodes.Status404NotFound, "message_not_found"),
        MessageRuleException { Code: MessageRuleError.Forbidden } =>
            (StatusCodes.Status403Forbidden, "forbidden"),
        MessageRuleException messageRuleException =>
            (StatusCodes.Status422UnprocessableEntity, ToSnakeCase(messageRuleException.Code.ToString())),
        MediaException { Code: MediaError.NotFound } =>
            (StatusCodes.Status404NotFound, "media_not_found"),
        MediaException { Code: MediaError.Forbidden } =>
            (StatusCodes.Status403Forbidden, "forbidden"),
        MediaException mediaException =>
            (StatusCodes.Status422UnprocessableEntity, ToSnakeCase(mediaException.Code.ToString())),
        MusicRuleException { Code: MusicRuleError.NotFound } =>
            (StatusCodes.Status404NotFound, "track_not_found"),
        MusicRuleException { Code: MusicRuleError.Forbidden } =>
            (StatusCodes.Status403Forbidden, "forbidden"),
        MusicRuleException musicException =>
            (StatusCodes.Status422UnprocessableEntity, ToSnakeCase(musicException.Code.ToString())),
        SupportRuleException { Code: SupportRuleError.NotFound } =>
            (StatusCodes.Status404NotFound, "support_ticket_not_found"),
        SupportRuleException { Code: SupportRuleError.Forbidden } =>
            (StatusCodes.Status403Forbidden, "forbidden"),
        SupportRuleException supportException =>
            (StatusCodes.Status422UnprocessableEntity, ToSnakeCase(supportException.Code.ToString())),
        AccountDeletionException { Code: AccountDeletionError.NotFound } =>
            (StatusCodes.Status404NotFound, "deletion_request_not_found"),
        AccountDeletionException deletionException =>
            (StatusCodes.Status422UnprocessableEntity, ToSnakeCase(deletionException.Code.ToString())),
        DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
            (StatusCodes.Status409Conflict, "phone_already_registered"),
        IOException =>
            (StatusCodes.Status503ServiceUnavailable, "email_delivery_unavailable"),
        FormatException =>
            (StatusCodes.Status422UnprocessableEntity, "validation_error"),
        _ => (StatusCodes.Status500InternalServerError, "internal_error")
    };

    var problem = new ProblemDetails
    {
        Status = status,
        Title = status >= 500 ? "An internal error occurred." : "The request could not be completed."
    };
    problem.Extensions["code"] = code;
    problem.Extensions["correlationId"] = context.TraceIdentifier;
    context.Response.StatusCode = status;
    context.Response.ContentType = "application/problem+json";
    await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
}));
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapAuthEndpoints();
app.MapProfileEndpoints();
app.MapContactEndpoints();
app.MapChatEndpoints();
app.MapChatFolderEndpoints();
app.MapMessageEndpoints();
app.MapUploadEndpoints();
app.MapMusicEndpoints();
app.MapSupportEndpoints();
app.MapHub<ChatHub>("/hubs/chat");
app.MapGet("/", () => Results.Ok(new { service = "messenger-api" }));

app.Run();

static string ToSnakeCase(string value) => string.Concat(value.Select((character, index) =>
    char.IsUpper(character) && index > 0
        ? $"_{char.ToLowerInvariant(character)}"
        : char.ToLowerInvariant(character).ToString()));

public partial class Program;
