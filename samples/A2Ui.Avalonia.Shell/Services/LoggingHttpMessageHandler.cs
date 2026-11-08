using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Blazejewicz.A2Ui.Avalonia.Shell.Services;

/// <summary>
/// Outbound <see cref="DelegatingHandler"/> for <see cref="IA2AClient"/> that emits structured logs
/// for HTTP requests (method, URL, status, byte counts, and duration).
/// </summary>
public sealed class LoggingHttpMessageHandler(ILogger<LoggingHttpMessageHandler> logger) : DelegatingHandler
{
    private readonly ILogger<LoggingHttpMessageHandler> _logger = logger;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        string method = request.Method.Method;
        string url = request.RequestUri?.ToString() ?? "(null)";

        long requestBytes = 0;
        if (request.Content is not null)
        {
            // Fall back to -1 when Content-Length is not populated.
            requestBytes = request.Content.Headers.ContentLength ?? -1;
        }

        LoggingHttpMessageHandlerLog.RequestStarted(this._logger, method, url, requestBytes);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            long responseBytes = response.Content.Headers.ContentLength ?? -1;
            LoggingHttpMessageHandlerLog.RequestCompleted(
                this._logger,
                method,
                url,
                (int)response.StatusCode,
                responseBytes,
                stopwatch.ElapsedMilliseconds
            );

            return response;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            LoggingHttpMessageHandlerLog.RequestFailed(
                this._logger,
                method,
                url,
                stopwatch.ElapsedMilliseconds,
                ex.GetType().Name,
                ex
            );
            throw;
        }
    }
}

internal static partial class LoggingHttpMessageHandlerLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "HTTP {Method} {Url} starting (requestBytes={RequestBytes})"
    )]
    public static partial void RequestStarted(ILogger logger, string method, string url, long requestBytes);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "HTTP {Method} {Url} completed: status={HttpStatus}, responseBytes={ResponseBytes}, durationMs={DurationMs}"
    )]
    public static partial void RequestCompleted(
        ILogger logger,
        string method,
        string url,
        int httpStatus,
        long responseBytes,
        long durationMs
    );

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "HTTP {Method} {Url} failed after {DurationMs}ms: {ExceptionType}"
    )]
    public static partial void RequestFailed(
        ILogger logger,
        string method,
        string url,
        long durationMs,
        string exceptionType,
        Exception exception
    );
}
