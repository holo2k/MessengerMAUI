using System.Security.Claims;
using Messenger.Application.Music;
using Messenger.Contracts.Media;
using Messenger.Contracts.Music;
using Messenger.Domain.Music;
using Messenger.Api.Hubs;
using Messenger.Contracts.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Messenger.Api.Endpoints;

public static class MusicEndpoints
{
    public static IEndpointRouteBuilder MapMusicEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var music = endpoints.MapGroup("/api/music").RequireAuthorization();
        music.MapGet("/search", async (string? query, ClaimsPrincipal p, IMusicService s, CancellationToken ct) => Results.Ok((await s.SearchAsync(UserId(p), query ?? "", ct)).Select(ToResponse))).Document("SearchMusic", "Найти музыку", "Ищет доступные треки по названию и исполнителю.", "Музыка");
        music.MapGet("/library", async (ClaimsPrincipal p, IMusicService s, CancellationToken ct) => Results.Ok((await s.ListLibraryAsync(UserId(p), ct)).Select(ToResponse))).Document("ListMusicLibrary", "Открыть медиатеку", "Возвращает треки, добавленные текущим пользователем.", "Музыка");
        music.MapPost("/tracks", async (CreateTrackRequest r, ClaimsPrincipal p, IMusicService s, CancellationToken ct) => Results.Ok(ToResponse(await s.CreateAsync(UserId(p), r.AudioObjectId, r.Title, r.Artist, r.DurationMs, r.CoverObjectId, ct)))).Document("CreateMusicTrack", "Загрузить трек", "Регистрирует загруженный аудиофайл и метаданные нового трека.", "Музыка");
        music.MapPost("/tracks/{id:guid}/declaration", async (Guid id, RightsDeclarationRequest r, ClaimsPrincipal p, IMusicService s, CancellationToken ct) => { await s.DeclareRightsAsync(UserId(p), id, r.Statement, ct); return Results.NoContent(); }).Document("DeclareMusicRights", "Подтвердить права на трек", "Сохраняет заявление загрузившего пользователя о наличии прав на публикацию.", "Музыка");
        music.MapPut("/library/{id:guid}", async (Guid id, ClaimsPrincipal p, IMusicService s, CancellationToken ct) => { await s.AddToLibraryAsync(UserId(p), id, ct); return Results.NoContent(); }).Document("AddMusicToLibrary", "Добавить трек в медиатеку", "Добавляет доступный трек в личную медиатеку.", "Музыка");
        music.MapDelete("/library/{id:guid}", async (Guid id, ClaimsPrincipal p, IMusicService s, CancellationToken ct) => { await s.RemoveFromLibraryAsync(UserId(p), id, ct); return Results.NoContent(); }).Document("RemoveMusicFromLibrary", "Удалить трек из медиатеки", "Убирает трек из личной медиатеки без удаления общего трека.", "Музыка");
        music.MapGet("/tracks/{id:guid}/stream", async (Guid id, ClaimsPrincipal p, IMusicService s, CancellationToken ct) => { var value = await s.AuthorizeStreamAsync(UserId(p), id, ct); return Results.Ok(new DownloadAuthorizationResponse(value.Url, value.ExpiresAt)); }).Document("AuthorizeMusicStream", "Получить ссылку для прослушивания", "Возвращает временную подписанную ссылку на поток трека.", "Музыка");
        music.MapGet("/tracks/{id:guid}/download", async (Guid id, ClaimsPrincipal p, IMusicService s, CancellationToken ct) => { var value = await s.AuthorizeStreamAsync(UserId(p), id, ct); return Results.Ok(new DownloadAuthorizationResponse(value.Url, value.ExpiresAt)); }).Document("AuthorizeMusicDownload", "Получить ссылку для скачивания", "Возвращает временную подписанную ссылку для скачивания трека.", "Музыка");
        music.MapPost("/tracks/{id:guid}/claims", async (Guid id, CopyrightClaimRequest r, ClaimsPrincipal p, IMusicService s, CancellationToken ct) => { await s.SubmitClaimAsync(UserId(p), id, r.Details, ct); return Results.Accepted(); }).Document("SubmitCopyrightClaim", "Отправить жалобу правообладателя", "Создаёт обращение о предполагаемом нарушении авторских прав.", "Музыка");
        endpoints.MapPut("/api/admin/music/tracks/{id:guid}", async (Guid id, ModerateTrackRequest r, ClaimsPrincipal p, IMusicModerationService s, IHubContext<ChatHub> hub, CancellationToken ct) =>
        {
            await s.DecideAsync(UserId(p), id, r.Action, r.Reason, ct);
            var status = r.Action.Equals("approve", StringComparison.OrdinalIgnoreCase) ? "available" : r.Action.ToLowerInvariant() == "delete" ? "deleted" : "blocked";
            await hub.Clients.All.SendAsync("MusicTrackStatusChanged", new MusicTrackStatusChangedEvent(id, status, r.Reason), ct);
            return Results.NoContent();
        }).RequireAuthorization().Document("ModerateMusicTrack", "Провести модерацию трека", "Административная модерация трека. Поле action: approve — одобрить и опубликовать, block — заблокировать, delete — удалить. Поле reason: понятная текстовая причина решения для журнала аудита. Готовые варианты тела запроса доступны в списке Examples.", "Администрирование музыки");
        return endpoints;
    }
    private static MusicTrackResponse ToResponse(MusicTrack x) => new(x.Id, x.Title, x.Artist, x.DurationMs, x.CoverObjectId, x.Status.ToString().ToLowerInvariant());
    private static Guid UserId(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier) ?? p.FindFirstValue("sub") ?? throw new UnauthorizedAccessException());
}
