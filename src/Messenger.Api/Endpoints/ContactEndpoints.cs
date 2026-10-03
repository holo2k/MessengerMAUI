using System.Security.Claims;
using Messenger.Application.Contacts;
using Messenger.Application.Users;
using Messenger.Contracts.Contacts;

namespace Messenger.Api.Endpoints;

public static class ContactEndpoints
{
    public static IEndpointRouteBuilder MapContactEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var contacts = endpoints.MapGroup("/api/contacts").RequireAuthorization();
        contacts.MapGet("/", async (
            string? cursor,
            int? limit,
            ClaimsPrincipal principal,
            IContactService service,
            CancellationToken ct) =>
        {
            var page = await service.ListAsync(UserId(principal), cursor, limit ?? 50, ct);
            return Results.Ok(new ContactPageResponse(page.Items.Select(ToResponse).ToArray(), page.NextCursor));
        });
        contacts.MapPost("/", async (
            CreateContactRequest request,
            ClaimsPrincipal principal,
            IContactService service,
            CancellationToken ct) =>
        {
            var ownerId = UserId(principal);
            var contact = await service.AddAsync(
                ownerId, request.TargetUserId, request.LocalFirstName, request.LocalLastName, ct);
            var view = new ContactView(contact, await service.GetUserAsync(ownerId, request.TargetUserId, ct));
            return Results.Created($"/api/contacts/{contact.Id}", ToResponse(view));
        });
        contacts.MapPatch("/{contactId:guid}", async (
            Guid contactId,
            UpdateContactRequest request,
            ClaimsPrincipal principal,
            IContactService service,
            CancellationToken ct) => Results.Ok(ToResponse(await service.UpdateAsync(
                UserId(principal), contactId, request.LocalFirstName, request.LocalLastName, request.IsMuted, ct))));
        contacts.MapDelete("/{contactId:guid}", async (
            Guid contactId,
            ClaimsPrincipal principal,
            IContactService service,
            CancellationToken ct) =>
        {
            await service.DeleteAsync(UserId(principal), contactId, ct);
            return Results.NoContent();
        });

        var users = endpoints.MapGroup("/api/users").RequireAuthorization();
        users.MapGet("/search", async (
            string query,
            ClaimsPrincipal principal,
            IContactService service,
            CancellationToken ct) => Results.Ok(new UserSearchPageResponse(
                (await service.SearchUsersAsync(UserId(principal), query, ct)).Select(ToSearchResponse).ToArray())));
        users.MapGet("/{userId:guid}", async (
            Guid userId,
            ClaimsPrincipal principal,
            IContactService service,
            CancellationToken ct) => Results.Ok(ToSearchResponse(
                await service.GetUserAsync(UserId(principal), userId, ct))));
        return endpoints;
    }

    private static ContactResponse ToResponse(ContactView view) => new(
        view.Contact.Id,
        view.Profile.UserId,
        view.Profile.FirstName,
        view.Profile.LastName,
        view.Profile.Username,
        view.Profile.Phone,
        view.Profile.AvatarObjectId,
        view.Profile.Bio,
        view.Contact.LocalFirstName,
        view.Contact.LocalLastName,
        view.Contact.IsMuted,
        view.Contact.CreatedAt);

    private static UserSearchResponse ToSearchResponse(VisibleProfile profile) => new(
        profile.UserId,
        profile.FirstName,
        profile.LastName,
        profile.Username,
        profile.Phone,
        profile.AvatarObjectId,
        profile.Bio);

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue("sub")
        ?? throw new UnauthorizedAccessException());
}
