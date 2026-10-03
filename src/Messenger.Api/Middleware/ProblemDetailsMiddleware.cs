using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.Api.Middleware;

public sealed class ProblemDetailsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);
        if (context.Response.HasStarted || context.Response.StatusCode < 400 || !string.IsNullOrEmpty(context.Response.ContentType)) return;
        var code = context.Response.StatusCode switch
        {
            StatusCodes.Status401Unauthorized => "unauthorized",
            StatusCodes.Status403Forbidden => "forbidden",
            StatusCodes.Status404NotFound => "not_found",
            StatusCodes.Status409Conflict => "conflict",
            StatusCodes.Status422UnprocessableEntity => "validation_error",
            StatusCodes.Status429TooManyRequests => "too_many_requests",
            _ => "request_failed"
        };
        var problem = new ProblemDetails { Status = context.Response.StatusCode, Title = "The request could not be completed." };
        problem.Extensions["code"] = code; problem.Extensions["correlationId"] = context.TraceIdentifier;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}
