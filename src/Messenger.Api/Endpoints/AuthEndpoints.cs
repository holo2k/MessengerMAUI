using System.Security.Claims;
using Messenger.Application.Identity;
using Messenger.Contracts.Auth;
using Messenger.Domain.Identity;
using Messenger.Contracts.Users;

namespace Messenger.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth");

        group.MapPost("/challenges", async (
            ChallengeRequest request,
            IAuthService auth,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<ChallengePurpose>(request.Purpose, true, out var purpose))
            {
                throw new FormatException("Unknown challenge purpose.");
            }

            var result = await auth.RequestChallengeAsync(
                new PhoneChallengeRequest(request.Phone, request.DefaultRegion, purpose),
                cancellationToken);
            return Results.Ok(new ChallengeResponse(result.ChallengeId, result.ExpiresAt));
        }).RequireRateLimiting("auth-challenge").Document("RequestAuthChallenge", "Запросить код подтверждения", "Создаёт SMS-задание для регистрации, входа или смены телефона. В Development возвращается тестовый код.", "Авторизация");

        group.MapPost("/register", async (
            CompleteChallengeRequest request,
            IAuthService auth,
            CancellationToken cancellationToken) =>
            ToResponse(await auth.RegisterAsync(
                new CompletePhoneChallengeRequest(request.ChallengeId, request.Code, request.DeviceLabel),
                cancellationToken))).RequireRateLimiting("auth-code").Document("Register", "Зарегистрироваться", "Создаёт пользователя по успешно подтверждённому номеру телефона и возвращает пару access/refresh токенов.", "Авторизация");

        group.MapPost("/login", async (
            CompleteChallengeRequest request,
            IAuthService auth,
            CancellationToken cancellationToken) =>
        {
            var result = await auth.LoginAsync(
                new CompletePhoneChallengeRequest(request.ChallengeId, request.Code, request.DeviceLabel),
                cancellationToken);
            return result.Session is not null
                ? ToResponse(result.Session)
                : Results.Accepted(value: new PendingTwoFactorResponse(
                    true,
                    result.PendingTwoFactor!.PendingToken,
                    result.PendingTwoFactor.ExpiresAt));
        }).RequireRateLimiting("auth-code").Document("Login", "Войти", "Завершает вход по телефонному коду; при включённой 2FA возвращает временный токен подтверждения.", "Авторизация");

        group.MapPost("/2fa/confirm", async (
            ConfirmLoginTwoFactorRequest request,
            IAuthService auth,
            CancellationToken cancellationToken) =>
            ToResponse(await auth.ConfirmTwoFactorAsync(request.PendingToken, request.Code, cancellationToken)))
            .RequireRateLimiting("auth-code").Document("ConfirmLoginTwoFactor", "Подтвердить двухфакторный вход", "Проверяет код из электронной почты и выдаёт пользовательскую сессию.", "Авторизация");

        group.MapPost("/refresh", async (
            RefreshRequest request,
            IAuthService auth,
            CancellationToken cancellationToken) =>
            ToResponse(await auth.RefreshAsync(request.RefreshToken, request.DeviceLabel, cancellationToken)))
            .Document("RefreshSession", "Обновить сессию", "Обменивает действительный refresh token на новую пару токенов с ротацией refresh token.", "Авторизация");

        group.MapPost("/logout", async (
            RefreshRequest request,
            IAuthService auth,
            CancellationToken cancellationToken) =>
        {
            await auth.LogoutAsync(request.RefreshToken, cancellationToken);
            return Results.NoContent();
        }).Document("Logout", "Завершить текущую сессию", "Отзывает переданный refresh token текущего устройства.", "Авторизация");

        group.MapPost("/logout-all", async (
            ClaimsPrincipal principal,
            IAuthService auth,
            CancellationToken cancellationToken) =>
        {
            var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? principal.FindFirstValue("sub")
                ?? throw new UnauthorizedAccessException();
            await auth.LogoutAllAsync(Guid.Parse(subject), cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization().Document("LogoutAll", "Завершить все сессии", "Требует Bearer JWT и отзывает все refresh token пользователя.", "Авторизация");

        return endpoints;
    }

    private static IResult ToResponse(AuthSessionResult result) => Results.Ok(new AuthSessionResponse(
        result.UserId,
        result.AccessToken,
        result.AccessTokenExpiresAt,
        result.RefreshToken,
        result.RefreshTokenExpiresAt));
}
