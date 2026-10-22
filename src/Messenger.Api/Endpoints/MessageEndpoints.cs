using System.Security.Claims;
using Messenger.Application.Chats;
using Messenger.Contracts.Messages;
using Messenger.Domain.Chats;
using Messenger.Api.Hubs;
using Messenger.Contracts.Realtime;
using Messenger.Application.Media;
using Messenger.Contracts.Music;
using Messenger.Contracts.Media;

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
        }).Document("ListMessages", "Получить сообщения", "Возвращает страницу сообщений выбранного чата по курсору.", "Сообщения");
        chats.MapPost("/{chatId:guid}/messages", async (
            Guid chatId,
            SendMessageRequest request,
            ClaimsPrincipal principal,
            IMessageService service,
            IChatEventPublisher publisher,
            IUploadService attachments,
            CancellationToken ct) =>
        {
            var kind = ParseKind(request.Type);
            if (kind is MessageKind.Voice or MessageKind.VideoNote && (request.AttachmentObjectIds is not { Count: 1 } || !string.IsNullOrWhiteSpace(request.Body)))
                throw new FormatException("A recording requires exactly one attachment and no caption.");
            if (kind == MessageKind.Music && (request.MusicTrackId is null || request.AttachmentObjectIds is { Count: > 0 }) ||
                kind != MessageKind.Music && request.MusicTrackId is not null)
                throw new FormatException("Invalid music message.");
            var sent = kind == MessageKind.Music
                ? await service.SendMusicAsync(UserId(principal), chatId, request.ClientMessageId, request.MusicTrackId!.Value, ct)
                : await service.SendAsync(UserId(principal), chatId, request.ClientMessageId, kind, request.Body, ct, request.DurationMs, request.Waveform);
            if (request.AttachmentObjectIds is { Count: > 0 })
            {
                await attachments.AttachToMessageAsync(
                    UserId(principal), sent.Message.Id, request.AttachmentObjectIds, ct);
                sent = await service.GetAsync(UserId(principal), sent.Message.Id, ct);
            }
            await publisher.MessageCreatedAsync(
                new MessageCreatedEvent(chatId, sent.Message.Id, sent.Message.Sequence), ct);
            return Results.Ok(ToResponse(sent));
        }).Document("SendMessage", "Отправить сообщение", "Создаёт сообщение, прикрепляет загруженные объекты и публикует событие SignalR.", "Сообщения");
        chats.MapGet("/{chatId:guid}/messages/search", async (
            Guid chatId,
            string query,
            int? limit,
            ClaimsPrincipal principal,
            IMessageSearchService service,
            CancellationToken ct) => Results.Ok(new MessagePageResponse(
                (await service.SearchAsync(UserId(principal), chatId, query, limit ?? 50, ct))
                    .Select(ToResponse).ToArray(), null)))
            .Document("SearchMessages", "Найти сообщения", "Ищет сообщения по тексту внутри выбранного чата.", "Сообщения");
        chats.MapGet("/{chatId:guid}/pins", async (
            Guid chatId,
            ClaimsPrincipal principal,
            IMessageService service,
            CancellationToken ct) => Results.Ok(new MessagePageResponse(
                (await service.ListPinsAsync(UserId(principal), chatId, ct)).Select(ToResponse).ToArray(), null)))
            .Document("ListPinnedMessages", "Получить закреплённые сообщения", "Возвращает закреплённые сообщения выбранного чата.", "Сообщения");
        var messages = endpoints.MapGroup("/api/messages").RequireAuthorization();
        messages.MapGet("/{messageId:guid}", async (Guid messageId, ClaimsPrincipal principal, IMessageService service, CancellationToken ct) =>
            Results.Ok(ToResponse(await service.GetAsync(UserId(principal), messageId, ct))))
            .Document("GetMessage", "Получить сообщение", "Возвращает доступное участнику сообщение с вложениями.", "Сообщения");
        messages.MapPost("/{messageId:guid}/forward", async (Guid messageId, ForwardMessageRequest request,
            ClaimsPrincipal principal, IMessageService service, IChatEventPublisher publisher, CancellationToken ct) =>
        {
            var forwarded = await service.ForwardAsync(UserId(principal), messageId, request.ChatId, request.ClientMessageId, ct);
            await publisher.MessageCreatedAsync(new MessageCreatedEvent(request.ChatId, forwarded.Message.Id, forwarded.Message.Sequence), ct);
            return Results.Ok(ToResponse(forwarded));
        }).Document("ForwardMessage", "Переслать сообщение", "Пересылает текст, вложения или песню в выбранный чат.", "Сообщения");
        messages.MapPatch("/{messageId:guid}", async (
            Guid messageId,
            EditMessageRequest request,
            ClaimsPrincipal principal,
            IMessageService service,
            IChatEventPublisher publisher,
            CancellationToken ct) =>
        {
            var updated = await service.EditAsync(UserId(principal), messageId, request.Body, ct);
            await publisher.MessageUpdatedAsync(new MessageUpdatedEvent(updated.Message.ChatId, messageId, updated.Message.Sequence), ct);
            return Results.Ok(ToResponse(updated));
        })
            .Document("EditMessage", "Изменить сообщение", "Редактирует текст сообщения при наличии прав автора.", "Сообщения");
        messages.MapDelete("/{messageId:guid}", async (
            Guid messageId,
            bool globally,
            ClaimsPrincipal principal,
            IMessageService service,
            IChatEventPublisher publisher,
            CancellationToken ct) =>
        {
            var deleted = await service.DeleteAsync(UserId(principal), messageId, globally, ct);
            if (globally)
                await publisher.MessageDeletedAsync(new MessageDeletedEvent(deleted.ChatId, messageId, deleted.Sequence), ct);
            else
                await publisher.MessageHiddenAsync(UserId(principal), new MessageHiddenEvent(deleted.ChatId, messageId), ct);
            return Results.NoContent();
        }).Document("DeleteMessage", "Удалить сообщение", "Удаляет сообщение локально либо для всех участников при наличии прав.", "Сообщения");
        messages.MapPut("/{messageId:guid}/pin", async (
            Guid messageId,
            ClaimsPrincipal principal,
            IMessageService service,
            CancellationToken ct) =>
        {
            await service.PinAsync(UserId(principal), messageId, ct);
            return Results.NoContent();
        }).Document("PinMessage", "Закрепить сообщение", "Закрепляет сообщение в соответствующем чате.", "Сообщения");
        messages.MapDelete("/{messageId:guid}/pin", async (
            Guid messageId,
            ClaimsPrincipal principal,
            IMessageService service,
            CancellationToken ct) =>
        {
            await service.UnpinAsync(UserId(principal), messageId, ct);
            return Results.NoContent();
        }).Document("UnpinMessage", "Открепить сообщение", "Снимает закрепление выбранного сообщения.", "Сообщения");
        return endpoints;
    }

    private static MessageResponse ToResponse(MessageView view) => new(
        view.Message.Id,
        view.Message.ChatId,
        view.Message.SenderUserId,
        view.Message.Sequence,
        view.Message.ClientMessageId,
        view.Message.Kind == MessageKind.VideoNote ? "video_note" : view.Message.Kind.ToString().ToLowerInvariant(),
        view.Body,
        view.IsPinned,
        view.Message.CreatedAt,
        view.Message.EditedAt,
        view.Message.DeletedAt,
        view.MusicTrack is { } track ? new MusicTrackResponse(track.Id, track.Title, track.Artist,
            track.DurationMs, track.CoverObjectId, track.Status.ToString().ToLowerInvariant(), track.UploaderUserId) : null,
        view.Attachments?.Select(value => new StoredObjectResponse(value.Id, value.FileName, value.ContentType,
            value.Size, value.Sha256, value.CreatedAt)).ToArray() ?? [], view.Message.IsForwarded, view.Message.DurationMs, view.Message.Waveform);

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
        "music" => MessageKind.Music,
        "voice" => MessageKind.Voice,
        "video_note" or "videonote" => MessageKind.VideoNote,
        _ => throw new FormatException("Unknown message type.")
    };

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue("sub")
        ?? throw new UnauthorizedAccessException());
}
