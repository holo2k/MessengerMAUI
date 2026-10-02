using System.Text.Json;
using System.Threading.RateLimiting;
using Messenger.Api.Configuration;
using Messenger.Api.Endpoints;
using Messenger.Application.Identity;
using Messenger.Application.Security;
using Messenger.Domain.Common;
using Messenger.Infrastructure;
using Messenger.Infrastructure.Identity;
using Messenger.Infrastructure.Persistence;
using Messenger.Infrastructure.Security;
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
builder.Services.AddSingleton(serviceProvider =>
    serviceProvider.GetRequiredService<IConfiguration>().GetSection("Auth").Get<AuthOptions>()
    ?? new AuthOptions());
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
        DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
            (StatusCodes.Status409Conflict, "phone_already_registered"),
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
app.MapGet("/", () => Results.Ok(new { service = "messenger-api" }));

app.Run();

static string ToSnakeCase(string value) => string.Concat(value.Select((character, index) =>
    char.IsUpper(character) && index > 0
        ? $"_{char.ToLowerInvariant(character)}"
        : char.ToLowerInvariant(character).ToString()));

public partial class Program;
