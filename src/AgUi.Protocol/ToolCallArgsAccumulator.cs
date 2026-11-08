using System.Diagnostics;
using System.Text;
using AGUI.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blazejewicz.A2Ui.AgUi.Protocol;

/// <summary>
/// Accumulates TOOL_CALL_ARGS delta strings per toolCallId.
/// Call <see cref="Complete"/> after TOOL_CALL_END to get final JSON.
/// </summary>
/// <remarks>
/// Not thread-safe. Mutate from a single writer or synchronize externally.
/// </remarks>
/// <param name="logger">Optional logger for accumulation diagnostics.</param>
public sealed class ToolCallArgsAccumulator(ILogger<ToolCallArgsAccumulator>? logger = null)
{
    private readonly Dictionary<string, StringBuilder> _buffers = [];
    private readonly ILogger _logger = logger ?? NullLogger<ToolCallArgsAccumulator>.Instance;

    /// <summary>Appends a TOOL_CALL_ARGS delta to the buffer for its tool call.</summary>
    /// <param name="args">The tool call args event containing the delta fragment.</param>
    public void OnArgs(ToolCallArgsEvent args)
    {
        string toolCallId = args.ToolCallId ?? string.Empty;
        string delta = args.Delta ?? string.Empty;

        if (!this._buffers.TryGetValue(toolCallId, out var sb))
        {
            ToolCallArgsAccumulatorLog.AccumulateStarted(this._logger, toolCallId);
            sb = new StringBuilder();
            this._buffers[toolCallId] = sb;
        }
        sb.Append(delta);
        ToolCallArgsAccumulatorLog.AccumulateProgress(this._logger, toolCallId, delta.Length, sb.Length);
    }

    /// <summary>Returns the complete JSON string and removes from buffer.</summary>
    public string Complete(string toolCallId)
    {
        using var activity = Diagnostics.Source.StartActivity("ToolCallArgs.Complete", ActivityKind.Internal);
        activity?.SetTag("a2ui.tool_call_id", toolCallId);

        if (this._buffers.Remove(toolCallId, out var sb))
        {
            var result = sb.ToString();
            activity?.SetTag("a2ui.total_length", result.Length);
            ToolCallArgsAccumulatorLog.AccumulateCompleted(this._logger, toolCallId, result.Length);
            return result;
        }

        activity?.SetTag("a2ui.total_length", 0);
        return string.Empty;
    }

    /// <summary>Returns whether there are buffered argument fragments for the given tool call.</summary>
    /// <param name="toolCallId">The tool call identifier to check.</param>
    /// <returns><see langword="true"/> if fragments are pending for the tool call.</returns>
    public bool HasPending(string toolCallId) => this._buffers.ContainsKey(toolCallId);

    /// <summary>Clears all pending buffers, logging any orphaned (never-completed) entries.</summary>
    public void Clear()
    {
        foreach (var kvp in this._buffers)
        {
            ToolCallArgsAccumulatorLog.AccumulateOrphaned(this._logger, kvp.Key, kvp.Value.Length);
        }
        this._buffers.Clear();
    }
}
