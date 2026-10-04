using System.Security.Claims;
using Messenger.Application.Users;
using Messenger.Application.Identity;
using Messenger.Contracts.Users;
using Messenger.Domain.Identity;
using Messenger.Domain.Users;
using Messenger.Application.Media;

namespace Messenger.Api.Endpoints;

public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/me").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal principal, IProfileService service, CancellationToken ct) =>
            ToResponse(await service.GetProfileAsync(UserId(principal), UserId(principal), ct)))
            .Document("GetMyProfile", "Получить свой профиль", "Возвращает полный профиль авторизованного пользователя.", "Профиль");

        group.MapPatch("/", async (UpdateMeRequest request, ClaimsPrincipal principal, IProfileService service, CancellationToken ct) =>
        {
            await service.UpdateProfileAsync(UserId(principal), new UpdateProfileRequest(
                request.FirstName, request.LastName, request.Username, request.Bio), ct);
            return Results.NoContent();
        }).Document("UpdateMyProfile", "Изменить профиль", "Обновляет имя, фамилию, username и биографию текущего пользователя.", "Профиль");

        group.MapPost("/avatar", async (AvatarRequest request, ClaimsPrincipal principal, IProfileService service, IUploadService uploads, CancellationToken ct) =>
        {
            var userId = UserId(principal);
            if (!Guid.TryParse(request.ObjectId, out var objectId))
            {
                throw new MediaException(MediaError.NotFound);
            }
            await uploads.ValidateOwnedAvailableAsync(userId, objectId, ct);
            await service.UpdateAvatarAsync(userId, objectId.ToString(), ct);
            return Results.NoContent();
        }).Document("SetMyAvatar", "Установить аватар", "Назначает ранее загруженное изображение аватаром текущего пользователя.", "Профиль");
        group.MapDelete("/avatar", async (ClaimsPrincipal principal, IProfileService service, CancellationToken ct) =>
        {
            await service.UpdateAvatarAsync(UserId(principal), null, ct);
            return Results.NoContent();
        }).Document("DeleteMyAvatar", "Удалить аватар", "Удаляет ссылку на текущий аватар пользователя.", "Профиль");

        group.MapGet("/privacy", async (ClaimsPrincipal principal, IProfileService service, CancellationToken ct) =>
        {
            var result = await service.GetPrivacyAsync(UserId(principal), ct);
            return Results.Ok(new PrivacyResponse(
                ToContract(result.Phone),
                ToContract(result.Avatar),
                ToContract(result.LastSeen)));
        }).Document("GetMyPrivacy", "Получить настройки приватности", "Возвращает правила видимости телефона, аватара и времени посещения.", "Приватность");
        group.MapPut("/privacy", async (PrivacyRequest request, ClaimsPrincipal principal, IProfileService service, CancellationToken ct) =>
        {
            await service.UpdatePrivacyAsync(UserId(principal), new PrivacyUpdateRequest(
                ParseVisibility(request.Phone), ParseVisibility(request.Avatar), ParseVisibility(request.LastSeen)), ct);
            return Results.NoContent();
        }).Document("UpdateMyPrivacy", "Изменить настройки приватности", "Сохраняет правила видимости полей профиля.", "Приватность");

        group.MapGet("/sessions", async (ClaimsPrincipal principal, IProfileService service, CancellationToken ct) =>
            Results.Ok((await service.GetSessionsAsync(UserId(principal), ct)).Select(session => new SessionResponse(
                session.Id, session.DeviceLabel, session.CreatedAt, session.ExpiresAt, session.IsActive))))
            .Document("ListMySessions", "Получить активные сессии", "Возвращает устройства и refresh-сессии текущего пользователя.", "Безопасность");
        group.MapDelete("/sessions/{sessionId:guid}", async (Guid sessionId, ClaimsPrincipal principal, IProfileService service, CancellationToken ct) =>
            await service.RevokeSessionAsync(UserId(principal), sessionId, ct) ? Results.NoContent() : Results.NotFound())
            .Document("RevokeMySession", "Отозвать сессию", "Завершает выбранную сессию текущего пользователя.", "Безопасность");

        group.MapPost("/2fa/email", async (BeginEmailTwoFactorRequest request, ClaimsPrincipal principal, ITwoFactorService service, CancellationToken ct) =>
        {
            var result = await service.BeginEmailSetupAsync(UserId(principal), request.Email, ct);
            return Results.Ok(new EmailChallengeResponse(result.ChallengeId, result.ExpiresAt));
        }).Document("BeginEmailTwoFactor", "Начать настройку 2FA", "Отправляет код подтверждения на указанный адрес электронной почты.", "Двухфакторная аутентификация");
        group.MapPost("/2fa/email/confirm", async (ConfirmEmailTwoFactorRequest request, ClaimsPrincipal principal, ITwoFactorService service, CancellationToken ct) =>
        {
            await service.ConfirmEmailAsync(UserId(principal), request.ChallengeId, request.Code, ct);
            return Results.NoContent();
        }).Document("ConfirmEmailTwoFactor", "Подтвердить настройку 2FA", "Проверяет почтовый код и включает двухфакторную аутентификацию.", "Двухфакторная аутентификация");
        group.MapPost("/2fa/disable", async (ClaimsPrincipal principal, ITwoFactorService service, CancellationToken ct) =>
        {
            await service.DisableAsync(UserId(principal), ct);
            return Results.NoContent();
        }).Document("DisableEmailTwoFactor", "Отключить 2FA", "Отключает двухфакторную аутентификацию текущего пользователя.", "Двухфакторная аутентификация");

        group.MapPost("/phone/challenges", async (
            BeginPhoneChangeRequest request,
            IAuthService auth,
            CancellationToken ct) =>
        {
            var result = await auth.RequestChallengeAsync(new PhoneChallengeRequest(
                request.Phone, request.DefaultRegion, ChallengePurpose.PhoneChange), ct);
            return Results.Ok(new EmailChallengeResponse(result.ChallengeId, result.ExpiresAt));
        }).RequireRateLimiting("auth-challenge").Document("RequestPhoneChangeChallenge", "Запросить смену телефона", "Создаёт код подтверждения для нового номера телефона.", "Профиль");

        group.MapPut("/phone", async (
            ChangePhoneRequest request,
            ClaimsPrincipal principal,
            IProfileService service,
            CancellationToken ct) =>
        {
            await service.ChangePhoneAsync(UserId(principal), request.ChallengeId, request.Code, ct);
            return Results.NoContent();
        }).RequireRateLimiting("auth-code").Document("ChangeMyPhone", "Изменить номер телефона", "Подтверждает код и назначает текущему пользователю новый уникальный номер.", "Профиль");

        return endpoints;
    }

    private static Guid UserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException();
        return Guid.Parse(value);
    }

    private static PrivacyVisibility ParseVisibility(string value) => value.ToLowerInvariant() switch
    {
        "everybody" => PrivacyVisibility.Everybody,
        "contacts" => PrivacyVisibility.Contacts,
        "nobody" => PrivacyVisibility.Nobody,
        _ => throw new FormatException("Unknown privacy visibility.")
    };

    private static string ToContract(PrivacyVisibility value) => value switch
    {
        PrivacyVisibility.Everybody => "everybody",
        PrivacyVisibility.Contacts => "contacts",
        PrivacyVisibility.Nobody => "nobody",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static MeResponse ToResponse(VisibleProfile profile) => new(
        profile.UserId, profile.FirstName, profile.LastName, profile.Username, profile.Bio,
        profile.Phone, profile.AvatarObjectId, profile.LastSeenAt);
}
