using System.Security.Claims;
using Messenger.Application.Chats;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Messenger.Api.Hubs;

[Authorize]
public sealed class ChatHub(IChatStore chats) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Guid.Parse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? Context.User?.FindFirstValue("sub")
            ?? throw new HubException("Unauthorized."));
        foreach (var chatId in await chats.ListActiveChatIdsAsync(userId, Context.ConnectionAborted))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(chatId), Context.ConnectionAborted);
        }
        await base.OnConnectedAsync();
    }

    public static string GroupName(Guid chatId) => $"chat:{chatId:N}";
}
