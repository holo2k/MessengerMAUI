using System.Security.Claims;
using Messenger.Application.Chats;
using Messenger.Contracts.Chats;
using Messenger.Domain.Chats;
using Messenger.Application.Media;

namespace Messenger.Api.Endpoints;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var chats = endpoints.MapGroup("/api/chats").RequireAuthorization();
        chats.MapGet("/", async (
            string? cursor,
            int? limit,
            ClaimsPrincipal principal,
            IChatService service,
            CancellationToken ct) =>
        {
            var page = await service.ListAsync(UserId(principal), cursor, limit ?? 50, ct);
            return Results.Ok(new ChatPageResponse(page.Items.Select(ToResponse).ToArray(), page.NextCursor));
        });
        chats.MapPost("/direct", async (
            CreateDirectChatRequest request,
            ClaimsPrincipal principal,
            IChatService service,
            CancellationToken ct) =>
        {
            var userId = UserId(principal);
            var chat = await service.CreateDirectAsync(userId, request.TargetUserId, ct);
            return Results.Ok(ToResponse(await service.GetAsync(userId, chat.Id, ct)));
        });
        chats.MapPost("/groups", async (
            CreateGroupChatRequest request,
            ClaimsPrincipal principal,
            IChatService service,
            CancellationToken ct) =>
        {
            var userId = UserId(principal);
            var chat = await service.CreateGroupAsync(userId, request.Title, request.MemberUserIds, ct);
            return Results.Created($"/api/chats/{chat.Id}", ToResponse(await service.GetAsync(userId, chat.Id, ct)));
        });
        chats.MapGet("/{chatId:guid}", async (
            Guid chatId,
            ClaimsPrincipal principal,
            IChatService service,
            CancellationToken ct) => Results.Ok(ToResponse(await service.GetAsync(UserId(principal), chatId, ct))));
        chats.MapPatch("/{chatId:guid}", async (
            Guid chatId,
            UpdateGroupChatRequest request,
            ClaimsPrincipal principal,
            IChatService service,
            IUploadService uploads,
            CancellationToken ct) =>
        {
            var actorId = UserId(principal);
            if (request.AvatarObjectId is not null)
            {
                if (!Guid.TryParse(request.AvatarObjectId, out var objectId))
                {
                    throw new MediaException(MediaError.NotFound);
                }
                await uploads.ValidateOwnedAvailableAsync(actorId, objectId, ct);
            }
            await service.UpdateGroupAsync(actorId, chatId, request.Title, request.AvatarObjectId, ct);
            return Results.NoContent();
        });
        chats.MapDelete("/{chatId:guid}", async (
            Guid chatId,
            ClaimsPrincipal principal,
            IChatService service,
            CancellationToken ct) =>
        {
            await service.DeleteOrLeaveAsync(UserId(principal), chatId, ct);
            return Results.NoContent();
        });
        chats.MapGet("/{chatId:guid}/members", async (
            Guid chatId,
            ClaimsPrincipal principal,
            IChatService service,
            CancellationToken ct) => Results.Ok((await service.ListMembersAsync(UserId(principal), chatId, ct))
                .Select(member => new ChatMemberResponse(member.UserId, Role(member.Role), member.JoinedAt))));
        chats.MapPost("/{chatId:guid}/members", async (
            Guid chatId,
            AddChatMemberRequest request,
            ClaimsPrincipal principal,
            IChatService service,
            CancellationToken ct) =>
        {
            await service.AddMemberAsync(UserId(principal), chatId, request.UserId, ct);
            return Results.NoContent();
        });
        chats.MapPatch("/{chatId:guid}/members/{userId:guid}", async (
            Guid chatId,
            Guid userId,
            UpdateChatMemberRequest request,
            ClaimsPrincipal principal,
            IChatService service,
            CancellationToken ct) =>
        {
            await service.ChangeMemberRoleAsync(UserId(principal), chatId, userId, ParseRole(request.Role), ct);
            return Results.NoContent();
        });
        chats.MapDelete("/{chatId:guid}/members/{userId:guid}", async (
            Guid chatId,
            Guid userId,
            ClaimsPrincipal principal,
            IChatService service,
            CancellationToken ct) =>
        {
            await service.RemoveMemberAsync(UserId(principal), chatId, userId, ct);
            return Results.NoContent();
        });
        chats.MapPut("/{chatId:guid}/archive", async (
            Guid chatId, ChatFlagRequest request, ClaimsPrincipal principal, IChatService service, CancellationToken ct) =>
        {
            await service.SetArchivedAsync(UserId(principal), chatId, request.Value, ct);
            return Results.NoContent();
        });
        chats.MapPut("/{chatId:guid}/mute", async (
            Guid chatId, ChatFlagRequest request, ClaimsPrincipal principal, IChatService service, CancellationToken ct) =>
        {
            await service.SetMutedAsync(UserId(principal), chatId, request.Value, ct);
            return Results.NoContent();
        });
        chats.MapPut("/{chatId:guid}/read", async (
            Guid chatId, ReadChatRequest request, ClaimsPrincipal principal, IChatService service, CancellationToken ct) =>
        {
            await service.MarkReadAsync(UserId(principal), chatId, request.Sequence, ct);
            return Results.NoContent();
        });
        return endpoints;
    }

    private static ChatResponse ToResponse(ChatView view) => new(
        view.Chat.Id,
        view.Chat.Type == ChatType.Direct ? "direct" : "group",
        view.Chat.Title,
        view.Chat.AvatarObjectId,
        Role(view.Member.Role),
        view.Member.IsMuted,
        view.Member.IsArchived,
        view.Member.HiddenAt is not null,
        view.Chat.CurrentMessageSequence,
        view.Chat.UpdatedAt);

    private static string Role(ChatMemberRole role) => role.ToString().ToLowerInvariant();
    private static ChatMemberRole ParseRole(string role) => role.ToLowerInvariant() switch
    {
        "member" => ChatMemberRole.Member,
        "admin" => ChatMemberRole.Admin,
        "owner" => ChatMemberRole.Owner,
        _ => throw new FormatException("Unknown member role.")
    };
    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue("sub")
        ?? throw new UnauthorizedAccessException());
}
