using System.Security.Claims;
using Messenger.Application.Chats;
using Messenger.Contracts.Messages;
using Messenger.Domain.Chats;
using Messenger.Api.Hubs;
using Messenger.Contracts.Realtime;
using Messenger.Application.Media;

namespace Messenger.Api.Endpoints;

public static class MessageEndpoints
{
    public static IEndpointRouteBuilder MapMessageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var chats = endpoints.MapGroup("/api/chats").RequireAuthorization();
        chats.MapGet("/{chatId:guid}/messages", async (
            Guid chatId,
            string? cursor,
            int? limit,
            ClaimsPrincipal principal,
            IMessageService service,
            CancellationToken ct) =>
        {
            var requested = Math.Clamp(limit ?? 50, 1, 99);
            var after = ParseCursor(cursor);
            var rows = await service.ListAsync(UserId(principal), chatId, after, requested + 1, ct);
            var page = rows.Take(requested).ToArray();
            return Results.Ok(new MessagePageResponse(
                page.Select(ToResponse).ToArray(),
                rows.Count > requested ? page[^1].Message.Sequence.ToString() : null));
        });
        chats.MapPost("/{chatId:guid}/messages", async (
            Guid chatId,
            SendMessageRequest request,
            ClaimsPrincipal principal,
            IMessageService service,
            IChatEventPublisher publisher,
            IUploadService attachments,
            CancellationToken ct) =>
        {
            var sent = await service.SendAsync(
                UserId(principal), chatId, request.ClientMessageId, ParseKind(request.Type), request.Body, ct);
            if (request.AttachmentObjectIds is { Count: > 0 })
            {
                await attachments.AttachToMessageAsync(
                    UserId(principal), sent.Message.Id, request.AttachmentObjectIds, ct);
            }
            await publisher.MessageCreatedAsync(
                new MessageCreatedEvent(chatId, sent.Message.Id, sent.Message.Sequence), ct);
            return Results.Ok(ToResponse(sent));
        });
        chats.MapGet("/{chatId:guid}/messages/search", async (
            Guid chatId,
            string query,
            int? limit,
            ClaimsPrincipal principal,
            IMessageSearchService service,
            CancellationToken ct) => Results.Ok(new MessagePageResponse(
                (await service.SearchAsync(UserId(principal), chatId, query, limit ?? 50, ct))
                    .Select(ToResponse).ToArray(), null)));
        chats.MapGet("/{chatId:guid}/pins", async (
            Guid chatId,
            ClaimsPrincipal principal,
            IMessageService service,
            CancellationToken ct) => Results.Ok(new MessagePageResponse(
                (await service.ListPinsAsync(UserId(principal), chatId, ct)).Select(ToResponse).ToArray(), null)));
        var messages = endpoints.MapGroup("/api/messages").RequireAuthorization();
        messages.MapPatch("/{messageId:guid}", async (
            Guid messageId,
            EditMessageRequest request,
            ClaimsPrincipal principal,
            IMessageService service,
            CancellationToken ct) => Results.Ok(ToResponse(
                await service.EditAsync(UserId(principal), messageId, request.Body, ct))));
        messages.MapDelete("/{messageId:guid}", async (
            Guid messageId,
            bool globally,
            ClaimsPrincipal principal,
            IMessageService service,
            CancellationToken ct) =>
        {
            await service.DeleteAsync(UserId(principal), messageId, globally, ct);
            return Results.NoContent();
        });
        messages.MapPut("/{messageId:guid}/pin", async (
            Guid messageId,
            ClaimsPrincipal principal,
            IMessageService service,
            CancellationToken ct) =>
        {
            await service.PinAsync(UserId(principal), messageId, ct);
            return Results.NoContent();
        });
        messages.MapDelete("/{messageId:guid}/pin", async (
            Guid messageId,
            ClaimsPrincipal principal,
            IMessageService service,
            CancellationToken ct) =>
        {
            await service.UnpinAsync(UserId(principal), messageId, ct);
            return Results.NoContent();
        });
        return endpoints;
    }

    private static MessageResponse ToResponse(MessageView view) => new(
        view.Message.Id,
        view.Message.ChatId,
        view.Message.SenderUserId,
        view.Message.Sequence,
        view.Message.ClientMessageId,
        view.Message.Kind.ToString().ToLowerInvariant(),
        view.Body,
        view.IsPinned,
        view.Message.CreatedAt,
        view.Message.EditedAt,
        view.Message.DeletedAt);

    private static long ParseCursor(string? cursor) => string.IsNullOrWhiteSpace(cursor)
        ? 0
        : long.TryParse(cursor, out var value) && value >= 0
            ? value
            : throw new FormatException("Invalid message cursor.");

    private static MessageKind ParseKind(string value) => value.ToLowerInvariant() switch
    {
        "text" => MessageKind.Text,
        "image" => MessageKind.Image,
        "video" => MessageKind.Video,
        "audio" => MessageKind.Audio,
        "file" => MessageKind.File,
        _ => throw new FormatException("Unknown message type.")
    };

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue("sub")
        ?? throw new UnauthorizedAccessException());
}
