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
        music.MapGet("/search", async (string? query, ClaimsPrincipal p, IMusicService s, CancellationToken ct) => Results.Ok((await s.SearchAsync(UserId(p), query ?? "", ct)).Select(ToResponse)));
        music.MapGet("/library", async (ClaimsPrincipal p, IMusicService s, CancellationToken ct) => Results.Ok((await s.ListLibraryAsync(UserId(p), ct)).Select(ToResponse)));
        music.MapPost("/tracks", async (CreateTrackRequest r, ClaimsPrincipal p, IMusicService s, CancellationToken ct) => Results.Ok(ToResponse(await s.CreateAsync(UserId(p), r.AudioObjectId, r.Title, r.Artist, r.DurationMs, r.CoverObjectId, ct))));
        music.MapPost("/tracks/{id:guid}/declaration", async (Guid id, RightsDeclarationRequest r, ClaimsPrincipal p, IMusicService s, CancellationToken ct) => { await s.DeclareRightsAsync(UserId(p), id, r.Statement, ct); return Results.NoContent(); });
        music.MapPut("/library/{id:guid}", async (Guid id, ClaimsPrincipal p, IMusicService s, CancellationToken ct) => { await s.AddToLibraryAsync(UserId(p), id, ct); return Results.NoContent(); });
        music.MapDelete("/library/{id:guid}", async (Guid id, ClaimsPrincipal p, IMusicService s, CancellationToken ct) => { await s.RemoveFromLibraryAsync(UserId(p), id, ct); return Results.NoContent(); });
        music.MapGet("/tracks/{id:guid}/stream", async (Guid id, ClaimsPrincipal p, IMusicService s, CancellationToken ct) => { var value = await s.AuthorizeStreamAsync(UserId(p), id, ct); return Results.Ok(new DownloadAuthorizationResponse(value.Url, value.ExpiresAt)); });
        music.MapGet("/tracks/{id:guid}/download", async (Guid id, ClaimsPrincipal p, IMusicService s, CancellationToken ct) => { var value = await s.AuthorizeStreamAsync(UserId(p), id, ct); return Results.Ok(new DownloadAuthorizationResponse(value.Url, value.ExpiresAt)); });
        music.MapPost("/tracks/{id:guid}/claims", async (Guid id, CopyrightClaimRequest r, ClaimsPrincipal p, IMusicService s, CancellationToken ct) => { await s.SubmitClaimAsync(UserId(p), id, r.Details, ct); return Results.Accepted(); });
        endpoints.MapPut("/api/admin/music/tracks/{id:guid}", async (Guid id, ModerateTrackRequest r, ClaimsPrincipal p, IMusicModerationService s, IHubContext<ChatHub> hub, CancellationToken ct) =>
        {
            await s.DecideAsync(UserId(p), id, r.Action, r.Reason, ct);
            var status = r.Action.Equals("approve", StringComparison.OrdinalIgnoreCase) ? "available" : r.Action.ToLowerInvariant() == "delete" ? "deleted" : "blocked";
            await hub.Clients.All.SendAsync("MusicTrackStatusChanged", new MusicTrackStatusChangedEvent(id, status, r.Reason), ct);
            return Results.NoContent();
        }).RequireAuthorization();
        return endpoints;
    }
    private static MusicTrackResponse ToResponse(MusicTrack x) => new(x.Id, x.Title, x.Artist, x.DurationMs, x.CoverObjectId, x.Status.ToString().ToLowerInvariant());
    private static Guid UserId(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier) ?? p.FindFirstValue("sub") ?? throw new UnauthorizedAccessException());
}
