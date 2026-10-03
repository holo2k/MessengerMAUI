namespace Messenger.Contracts.Contacts;

public sealed record CreateContactRequest(Guid TargetUserId, string? LocalFirstName, string? LocalLastName);
public sealed record UpdateContactRequest(string? LocalFirstName, string? LocalLastName, bool IsMuted);
public sealed record ContactResponse(
    Guid Id,
    Guid UserId,
    string FirstName,
    string LastName,
    string? Username,
    string? Phone,
    string? AvatarObjectId,
    string? Bio,
    string? LocalFirstName,
    string? LocalLastName,
    bool IsMuted,
    DateTimeOffset CreatedAt);
public sealed record ContactPageResponse(IReadOnlyList<ContactResponse> Items, string? NextCursor);
public sealed record UserSearchResponse(
    Guid UserId,
    string FirstName,
    string LastName,
    string? Username,
    string? Phone,
    string? AvatarObjectId,
    string? Bio);
public sealed record UserSearchPageResponse(IReadOnlyList<UserSearchResponse> Items);
