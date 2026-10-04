using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Messenger.Api.Swagger;

public sealed class MusicModerationOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!string.Equals(operation.OperationId, "ModerateMusicTrack", StringComparison.Ordinal))
        {
            return;
        }

        var content = operation.RequestBody?.Content;
        if (content is null || !content.TryGetValue("application/json", out var mediaType) || mediaType is null)
        {
            return;
        }

        mediaType.Examples = new Dictionary<string, IOpenApiExample>
        {
            ["approve"] = Example("Одобрить", "Трек прошёл проверку и становится доступен.", "approve", "Права подтверждены"),
            ["block"] = Example("Заблокировать", "Трек скрывается, например, на время разбирательства.", "block", "Получена жалоба правообладателя"),
            ["delete"] = Example("Удалить", "Трек помечается удалённым и больше не выдаётся пользователям.", "delete", "Нарушение авторских прав подтверждено")
        };
    }

    private static OpenApiExample Example(string summary, string description, string action, string reason) => new()
    {
        Summary = summary,
        Description = description,
        Value = new JsonObject
        {
            ["action"] = action,
            ["reason"] = reason
        }
    };
}
