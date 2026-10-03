namespace Messenger.Api.Middleware;

public sealed class SafeRequestLoggingMiddleware(RequestDelegate next, ILogger<SafeRequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        await next(context);
        logger.LogInformation(
            "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs:F1} ms ({CorrelationId})",
            context.Request.Method,
            SensitiveDataLoggingFilter.Redact(context.Request.Path.Value ?? "/"),
            context.Response.StatusCode,
            System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            context.TraceIdentifier);
    }
}
