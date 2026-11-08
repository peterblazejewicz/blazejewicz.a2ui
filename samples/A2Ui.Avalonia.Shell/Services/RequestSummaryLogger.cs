using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Blazejewicz.A2Ui.Avalonia.Shell.Services;

/// <summary>
/// Subscribes to the <c>A2Ui.Shell.A2AClient</c> <see cref="ActivitySource"/> and logs a
/// single-line summary when an <c>A2A.SendMessage</c> <see cref="Activity"/> stops.
/// </summary>
public sealed class RequestSummaryLogger : IDisposable
{
    private const string A2AClientSourceName = "A2Ui.Shell.A2AClient";
    private const string SendMessageActivityName = "A2A.SendMessage";

    private readonly ILogger<RequestSummaryLogger> _logger;
    private readonly ActivityListener _listener;

    public RequestSummaryLogger(ILogger<RequestSummaryLogger> logger)
    {
        this._logger = logger;
        this._listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == A2AClientSourceName,
            Sample = (ref _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref _) => ActivitySamplingResult.AllData,
            ActivityStopped = this.OnActivityStopped,
        };
        ActivitySource.AddActivityListener(this._listener);
    }

    private void OnActivityStopped(Activity activity)
    {
        if (activity.OperationName != SendMessageActivityName)
        {
            return;
        }

        string correlationId = GetTag(activity, "a2a.correlation_id") ?? "(none)";
        string httpUrl = GetTag(activity, "http.url") ?? "(none)";
        int httpStatus = TryParseInt(GetTag(activity, "http.status_code")) ?? 0;
        int messageCount = TryParseInt(GetTag(activity, "a2ui.message_count")) ?? 0;
        long durationMs = (long)activity.Duration.TotalMilliseconds;
        string status = activity.Status == ActivityStatusCode.Error ? "failed" : "ok";

        RequestSummaryLoggerLog.RequestSummary(
            this._logger,
            status,
            httpUrl,
            httpStatus,
            messageCount,
            durationMs,
            correlationId
        );
    }

    private static string? GetTag(Activity activity, string tagName)
    {
        foreach (var tag in activity.TagObjects)
        {
            if (tag.Key == tagName)
            {
                return tag.Value?.ToString();
            }
        }
        return null;
    }

    private static int? TryParseInt(string? s) => int.TryParse(s, CultureInfo.InvariantCulture, out int v) ? v : null;

    public void Dispose() => this._listener.Dispose();
}

internal static partial class RequestSummaryLoggerLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "RequestSummary: {Status} {HttpUrl} → httpStatus={HttpStatus}, messageCount={MessageCount}, durationMs={DurationMs}, correlationId={CorrelationId}"
    )]
    public static partial void RequestSummary(
        ILogger logger,
        string status,
        string httpUrl,
        int httpStatus,
        int messageCount,
        long durationMs,
        string correlationId
    );
}
