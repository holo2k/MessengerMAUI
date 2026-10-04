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
            Results.Ok(ToResponse(await service.CreateAsync(UserId(principal), request.Subject, request.Body, ct))))
            .Document("CreateSupportTicket", "Создать обращение", "Создаёт обращение пользователя в службу поддержки.", "Поддержка");
        support.MapGet("/tickets", async (ClaimsPrincipal principal, ISupportService service, CancellationToken ct) =>
            Results.Ok((await service.ListAsync(UserId(principal), ct)).Select(ToResponse)))
            .Document("ListSupportTickets", "Получить обращения", "Возвращает обращения текущего пользователя.", "Поддержка");
        support.MapGet("/tickets/{ticketId:guid}", async (Guid ticketId, ClaimsPrincipal principal, ISupportService service, CancellationToken ct) =>
            Results.Ok(ToResponse(await service.GetAsync(UserId(principal), ticketId, ct))))
            .Document("GetSupportTicket", "Открыть обращение", "Возвращает обращение и историю сообщений текущего пользователя.", "Поддержка");
        support.MapPost("/tickets/{ticketId:guid}/messages", async (Guid ticketId, SupportReplyRequest request, ClaimsPrincipal principal, ISupportService service, CancellationToken ct) =>
        { await service.ReplyAsync(UserId(principal), ticketId, request.Body, ct); return Results.NoContent(); })
            .Document("ReplySupportTicket", "Ответить в обращении", "Добавляет сообщение пользователя в открытое обращение.", "Поддержка");

        var admin = endpoints.MapGroup("/api/admin/support").RequireAuthorization();
        admin.MapGet("/tickets", async (ClaimsPrincipal principal, ISupportService service, CancellationToken ct) =>
            Results.Ok((await service.ListAllAsync(UserId(principal), ct)).Select(ToResponse)))
            .Document("ListAllSupportTickets", "Получить все обращения", "Административный список обращений всех пользователей.", "Администрирование поддержки");
        admin.MapGet("/tickets/{ticketId:guid}", async (Guid ticketId, ClaimsPrincipal principal, ISupportService service, CancellationToken ct) =>
            Results.Ok(ToResponse(await service.GetAsync(UserId(principal), ticketId, ct))))
            .Document("GetAnySupportTicket", "Открыть обращение пользователя", "Административно возвращает выбранное обращение и переписку.", "Администрирование поддержки");
        admin.MapPost("/tickets/{ticketId:guid}/messages", async (Guid ticketId, SupportReplyRequest request, ClaimsPrincipal principal, ISupportService service, CancellationToken ct) =>
        { await service.ReplyAsStaffAsync(UserId(principal), ticketId, request.Body, ct); return Results.NoContent(); })
            .Document("ReplySupportTicketAsStaff", "Ответить от поддержки", "Добавляет официальный ответ сотрудника в обращение.", "Администрирование поддержки");
        admin.MapPost("/tickets/{ticketId:guid}/close", async (Guid ticketId, ClaimsPrincipal principal, ISupportService service, CancellationToken ct) =>
        { await service.CloseAsync(UserId(principal), ticketId, ct); return Results.NoContent(); })
            .Document("CloseSupportTicket", "Закрыть обращение", "Административно закрывает обращение пользователя.", "Администрирование поддержки");

        var deletion = endpoints.MapGroup("/api/me/deletion").RequireAuthorization();
        deletion.MapPost("/", async (ClaimsPrincipal principal, IAccountDeletionService service, CancellationToken ct) =>
        { var result = await service.RequestAsync(UserId(principal), ct); return Results.Ok(new AccountDeletionResponse(result.Id, result.RequestedAt, result.ExecuteAt)); })
            .Document("RequestAccountDeletion", "Запросить удаление аккаунта", "Создаёт отложенную заявку на удаление аккаунта текущего пользователя.", "Аккаунт");
        deletion.MapDelete("/", async (ClaimsPrincipal principal, IAccountDeletionService service, CancellationToken ct) =>
        { await service.CancelAsync(UserId(principal), ct); return Results.NoContent(); })
            .Document("CancelAccountDeletion", "Отменить удаление аккаунта", "Отменяет активную отложенную заявку на удаление аккаунта.", "Аккаунт");
        return endpoints;
    }

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub") ?? throw new UnauthorizedAccessException());
    private static SupportTicketResponse ToResponse(SupportTicket x) => new(x.Id, x.OwnerUserId, x.Subject, x.Status.ToString().ToLowerInvariant(), x.CreatedAt, x.ClosedAt);
    private static SupportConversationResponse ToResponse(SupportConversation x) => new(ToResponse(x.Ticket), x.Messages.Select(m => new SupportMessageResponse(m.Id, m.AuthorUserId, m.Body, m.IsStaff, m.CreatedAt)).ToArray());
}
