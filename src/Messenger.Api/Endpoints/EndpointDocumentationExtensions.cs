namespace Messenger.Api.Endpoints;

internal static class EndpointDocumentationExtensions
{
    public static RouteHandlerBuilder Document(
        this RouteHandlerBuilder endpoint,
        string operationId,
        string summary,
        string description,
        string tag) => endpoint
        .WithName(operationId)
        .WithSummary(summary)
        .WithDescription(description)
        .WithTags(tag);
}
