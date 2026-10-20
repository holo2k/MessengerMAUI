using System.Security.Claims;
using Messenger.Application.Chats;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Messenger.Api.Hubs;

[Authorize]
public sealed class ChatHub(IChatStore chats) : Hub
{
    public Task Ready() => Task.CompletedTask;

    public async Task JoinChat(Guid chatId)
    {
        var userId = UserId();
        var chat = await chats.FindChatAsync(chatId, Context.ConnectionAborted);
        var member = await chats.FindMemberAsync(chatId, userId, Context.ConnectionAborted);
        if (chat is null || chat.DeletedAt is not null || member is not { IsActive: true })
            throw new HubException("Chat not found.");
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(chatId), Context.ConnectionAborted);
    }

    public override async Task OnConnectedAsync()
    {
        var userId = UserId();
        foreach (var chatId in await chats.ListActiveChatIdsAsync(userId, Context.ConnectionAborted))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(chatId), Context.ConnectionAborted);
        }
        await base.OnConnectedAsync();
    }

    private Guid UserId() => Guid.Parse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? Context.User?.FindFirstValue("sub")
        ?? throw new HubException("Unauthorized."));

    public static string GroupName(Guid chatId) => $"chat:{chatId:N}";
}
