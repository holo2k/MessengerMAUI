using System.Security.Claims;
using Messenger.Application.Accounts;
using Messenger.Application.Support;
using Messenger.Contracts.Support;
using Messenger.Domain.Support;

namespace Messenger.Api.Endpoints;

public static class SupportEndpoints
{
    public static IEndpointRouteBuilder MapSupportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var support = endpoints.MapGroup("/api/support").RequireAuthorization();
        support.MapPost("/tickets", async (CreateSupportTicketRequest request, ClaimsPrincipal principal, ISupportService service, CancellationToken ct) =>
            Results.Ok(ToResponse(await service.CreateAsync(UserId(principal), request.Subject, request.Body, ct))));
        support.MapGet("/tickets", async (ClaimsPrincipal principal, ISupportService service, CancellationToken ct) =>
            Results.Ok((await service.ListAsync(UserId(principal), ct)).Select(ToResponse)));
        support.MapGet("/tickets/{ticketId:guid}", async (Guid ticketId, ClaimsPrincipal principal, ISupportService service, CancellationToken ct) =>
            Results.Ok(ToResponse(await service.GetAsync(UserId(principal), ticketId, ct))));
        support.MapPost("/tickets/{ticketId:guid}/messages", async (Guid ticketId, SupportReplyRequest request, ClaimsPrincipal principal, ISupportService service, CancellationToken ct) =>
        { await service.ReplyAsync(UserId(principal), ticketId, request.Body, ct); return Results.NoContent(); });

        var admin = endpoints.MapGroup("/api/admin/support").RequireAuthorization();
        admin.MapGet("/tickets", async (ClaimsPrincipal principal, ISupportService service, CancellationToken ct) =>
            Results.Ok((await service.ListAllAsync(UserId(principal), ct)).Select(ToResponse)));
        admin.MapGet("/tickets/{ticketId:guid}", async (Guid ticketId, ClaimsPrincipal principal, ISupportService service, CancellationToken ct) =>
            Results.Ok(ToResponse(await service.GetAsync(UserId(principal), ticketId, ct))));
        admin.MapPost("/tickets/{ticketId:guid}/messages", async (Guid ticketId, SupportReplyRequest request, ClaimsPrincipal principal, ISupportService service, CancellationToken ct) =>
        { await service.ReplyAsStaffAsync(UserId(principal), ticketId, request.Body, ct); return Results.NoContent(); });
        admin.MapPost("/tickets/{ticketId:guid}/close", async (Guid ticketId, ClaimsPrincipal principal, ISupportService service, CancellationToken ct) =>
        { await service.CloseAsync(UserId(principal), ticketId, ct); return Results.NoContent(); });

        var deletion = endpoints.MapGroup("/api/me/deletion").RequireAuthorization();
        deletion.MapPost("/", async (ClaimsPrincipal principal, IAccountDeletionService service, CancellationToken ct) =>
        { var result = await service.RequestAsync(UserId(principal), ct); return Results.Ok(new AccountDeletionResponse(result.Id, result.RequestedAt, result.ExecuteAt)); });
        deletion.MapDelete("/", async (ClaimsPrincipal principal, IAccountDeletionService service, CancellationToken ct) =>
        { await service.CancelAsync(UserId(principal), ct); return Results.NoContent(); });
        return endpoints;
    }

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub") ?? throw new UnauthorizedAccessException());
    private static SupportTicketResponse ToResponse(SupportTicket x) => new(x.Id, x.OwnerUserId, x.Subject, x.Status.ToString().ToLowerInvariant(), x.CreatedAt, x.ClosedAt);
    private static SupportConversationResponse ToResponse(SupportConversation x) => new(ToResponse(x.Ticket), x.Messages.Select(m => new SupportMessageResponse(m.Id, m.AuthorUserId, m.Body, m.IsStaff, m.CreatedAt)).ToArray());
}
