using System.Security.Claims;
using Messenger.Application.Media;
using Messenger.Contracts.Media;
using Messenger.Domain.Media;

namespace Messenger.Api.Endpoints;

public static class UploadEndpoints
{
    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var uploads = endpoints.MapGroup("/api/uploads").RequireAuthorization();
        uploads.MapPost("/", async (CreateUploadRequest request, ClaimsPrincipal principal, IUploadService service, CancellationToken ct) =>
        {
            var value = await service.CreateAsync(UserId(principal),
                new CreateUploadCommand(request.FileName, request.ContentType, request.Size, request.Sha256), ct);
            return Results.Ok(new UploadSessionResponse(value.Id, value.ObjectKey, value.UploadUrl, value.ExpiresAt));
        });
        uploads.MapPost("/{uploadId:guid}/complete", async (
            Guid uploadId, ClaimsPrincipal principal, IUploadService service, CancellationToken ct) =>
                Results.Ok(ToResponse(await service.CompleteAsync(UserId(principal), uploadId, ct))));

        endpoints.MapGet("/api/objects/{objectId:guid}/download", async (
            Guid objectId, ClaimsPrincipal principal, IUploadService service, CancellationToken ct) =>
        {
            var value = await service.AuthorizeDownloadAsync(UserId(principal), objectId, ct);
            return Results.Ok(new DownloadAuthorizationResponse(value.Url, value.ExpiresAt));
        }).RequireAuthorization();

        endpoints.MapGet("/api/chats/{chatId:guid}/media", async (
            Guid chatId, ClaimsPrincipal principal, IUploadService service, CancellationToken ct) =>
                Results.Ok((await service.ListChatMediaAsync(UserId(principal), chatId, ct)).Select(ToResponse)))
            .RequireAuthorization();
        return endpoints;
    }

    private static StoredObjectResponse ToResponse(StoredObject value) =>
        new(value.Id, value.FileName, value.ContentType, value.Size, value.Sha256, value.CreatedAt);

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue("sub")
        ?? throw new UnauthorizedAccessException());
}
