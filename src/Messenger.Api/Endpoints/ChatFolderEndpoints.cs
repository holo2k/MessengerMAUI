using System.Security.Claims;
using Messenger.Application.Chats;
using Messenger.Contracts.Chats;

namespace Messenger.Api.Endpoints;

public static class ChatFolderEndpoints
{
    public static IEndpointRouteBuilder MapChatFolderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var folders = endpoints.MapGroup("/api/chat-folders").RequireAuthorization();
        folders.MapGet("/", async (ClaimsPrincipal principal, IChatFolderService service, CancellationToken ct) =>
            Results.Ok((await service.ListAsync(UserId(principal), ct)).Select(ToResponse)))
            .Document("ListChatFolders", "Получить папки чатов", "Возвращает папки и включённые в них чаты в пользовательском порядке.", "Папки чатов");
        folders.MapPost("/", async (
            CreateChatFolderRequest request,
            ClaimsPrincipal principal,
            IChatFolderService service,
            CancellationToken ct) =>
        {
            var folder = await service.CreateAsync(UserId(principal), request.Title, ct);
            return Results.Created($"/api/chat-folders/{folder.Folder.Id}", ToResponse(folder));
        }).Document("CreateChatFolder", "Создать папку чатов", "Создаёт новую пользовательскую папку чатов.", "Папки чатов");
        folders.MapPatch("/{folderId:guid}", async (
            Guid folderId,
            UpdateChatFolderRequest request,
            ClaimsPrincipal principal,
            IChatFolderService service,
            CancellationToken ct) =>
        {
            await service.UpdateAsync(UserId(principal), folderId, request.Title, ct);
            return Results.NoContent();
        }).Document("UpdateChatFolder", "Переименовать папку", "Изменяет название выбранной папки чатов.", "Папки чатов");
        folders.MapDelete("/{folderId:guid}", async (
            Guid folderId,
            ClaimsPrincipal principal,
            IChatFolderService service,
            CancellationToken ct) =>
        {
            await service.DeleteAsync(UserId(principal), folderId, ct);
            return Results.NoContent();
        }).Document("DeleteChatFolder", "Удалить папку", "Удаляет папку, не удаляя находящиеся в ней чаты.", "Папки чатов");
        folders.MapPut("/order", async (
            ReorderChatFoldersRequest request,
            ClaimsPrincipal principal,
            IChatFolderService service,
            CancellationToken ct) =>
        {
            await service.ReorderAsync(UserId(principal), request.FolderIds, ct);
            return Results.NoContent();
        }).Document("ReorderChatFolders", "Изменить порядок папок", "Сохраняет полный пользовательский порядок папок чатов.", "Папки чатов");
        folders.MapPut("/{folderId:guid}/chats", async (
            Guid folderId,
            SetChatFolderChatsRequest request,
            ClaimsPrincipal principal,
            IChatFolderService service,
            CancellationToken ct) =>
        {
            await service.SetChatsAsync(UserId(principal), folderId, request.ChatIds, ct);
            return Results.NoContent();
        }).Document("SetChatFolderChats", "Изменить состав папки", "Заменяет список чатов, входящих в выбранную папку.", "Папки чатов");
        return endpoints;
    }

    private static ChatFolderResponse ToResponse(ChatFolderView view) => new(
        view.Folder.Id, view.Folder.Title, view.Folder.Position, view.ChatIds);

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue("sub")
        ?? throw new UnauthorizedAccessException());
}
