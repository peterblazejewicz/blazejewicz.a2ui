using System.Text.Json;
using Avalonia.Controls;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Validation;
using Microsoft.Extensions.Logging;

namespace Blazejewicz.A2Ui.Avalonia.Catalog.Entries;

/// <summary>
/// Evaluates <see cref="CheckRule"/> conditions and wraps a control with
/// inline validation error messages when any check fails.
/// </summary>
internal static class CheckHelper
{
    /// <summary>
    /// Evaluate checks and wrap the control with error messages if any fail.
    /// Returns the original control if no checks exist or all pass.
    /// Always wraps in a StackPanel when checks are defined (even if all pass)
    /// so that <see cref="UpdateChecks"/> can add/remove errors in place.
    /// </summary>
    public static Control ApplyChecks(Control control, A2UiComponent c, IRenderContext ctx)
    {
        if (c.Checks is not { Length: > 0 })
        {
            return control;
        }

        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(control);
        AddErrorTextBlocks(panel, c, ctx);
        return panel;
    }

    /// <summary>
    /// Re-evaluate checks and update error TextBlocks in an existing wrapper panel.
    /// Removes old error messages and adds current ones, preserving the first child
    /// (the actual input control) and its focus state.
    /// </summary>
    public static void UpdateChecks(StackPanel wrapper, A2UiComponent c, IRenderContext ctx)
    {
        // Remove old error TextBlocks (everything after the first child)
        while (wrapper.Children.Count > 1)
        {
            wrapper.Children.RemoveAt(wrapper.Children.Count - 1);
        }

        AddErrorTextBlocks(wrapper, c, ctx);
    }

    /// <summary>
    /// Try to find the inner input control inside a CheckHelper wrapper panel.
    /// Returns null if <paramref name="existing"/> is not a check wrapper.
    /// </summary>
    public static T? FindInner<T>(Control existing)
        where T : Control
    {
        if (existing is T direct)
        {
            return direct;
        }

        if (existing is StackPanel panel && panel.Children.Count > 0 && panel.Children[0] is T inner)
        {
            return inner;
        }

        return null;
    }

    /// <summary>
    /// Tag-aware variant of <see cref="FindInner{T}(Control)"/>. Returns the inner
    /// control only when its <see cref="Control.Tag"/> equals <paramref name="tag"/>.
    /// Needed when the check wrapper and the inner container are both the same type
    /// (e.g. DateTimeInput's composite picker wraps a tagged StackPanel inside an
    /// untagged wrapper StackPanel): without the tag filter, the direct-match
    /// shortcut would return the wrapper and the caller would miss the inner panel.
    /// </summary>
    public static T? FindInner<T>(Control existing, object tag)
        where T : Control
    {
        if (existing is T direct && Equals(direct.Tag, tag))
        {
            return direct;
        }

        if (
            existing is StackPanel panel
            && panel.Children.Count > 0
            && panel.Children[0] is T inner
            && Equals(inner.Tag, tag)
        )
        {
            return inner;
        }

        return null;
    }

    /// <summary>
    /// Displays the first failing check message for the component.
    /// </summary>
    private static void AddErrorTextBlocks(StackPanel panel, A2UiComponent c, IRenderContext ctx)
    {
        if (c.Checks is not { Length: > 0 })
        {
            return;
        }

        foreach (CheckRule check in c.Checks)
        {
            string? raw = EvaluateCondition(check, ctx);
            var (isValid, message, severity) = ParseCheckResult(raw, check);
            if (!isValid)
            {
                string styleClass = severity.ToLowerInvariant() switch
                {
                    "warning" => "ValidationWarning",
                    "info" => "ValidationInfo",
                    _ => "ValidationError",
                };

                panel.Children.Add(
                    new TextBlock
                    {
                        Text = message ?? string.Empty,
                        Classes = { styleClass },
                        FontSize = 12,
                    }
                );
                break; // Only show the first failing message
            }
        }
    }

    /// <summary>
    /// Evaluate all checks on a component. Returns <c>true</c> when every check
    /// passes or has non-error severity ("warning", "info"), or when no checks are defined.
    /// Only "error" severity checks block validity.
    /// </summary>
    public static bool AllChecksPassing(A2UiComponent c, IRenderContext ctx)
    {
        if (c.Checks is not { Length: > 0 })
        {
            return true;
        }

        foreach (CheckRule check in c.Checks)
        {
            string? raw = EvaluateCondition(check, ctx);
            var (isValid, _, severity) = ParseCheckResult(raw, check);
            if (!isValid && string.Equals(severity, "error", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Return the message of the first failing check, or <c>null</c> if all pass.
    /// </summary>
    public static string? FirstFailingMessage(A2UiComponent c, IRenderContext ctx)
    {
        if (c.Checks is not { Length: > 0 })
        {
            return null;
        }

        foreach (CheckRule check in c.Checks)
        {
            string? raw = EvaluateCondition(check, ctx);
            var (isValid, message, _) = ParseCheckResult(raw, check);
            if (!isValid)
            {
                return message;
            }
        }

        return null;
    }

    private static (bool IsValid, string? Message, string Severity) ParseCheckResult(string? raw, CheckRule check)
    {
        if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
        {
            return (true, null, "error");
        }

        if (raw?.Length > 2 && raw[0] == '{' && raw[^1] == '}')
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (
                    root.TryGetProperty("valid", out var validEl)
                    && (validEl.ValueKind == JsonValueKind.True || validEl.ValueKind == JsonValueKind.False)
                )
                {
                    bool valid = validEl.GetBoolean();
                    string? msg = root.TryGetProperty("message", out var mEl) ? mEl.GetString() : null;
                    if (string.IsNullOrEmpty(msg))
                    {
                        msg = check.Message;
                    }

                    string sev = root.TryGetProperty("severity", out var sEl) ? (sEl.GetString() ?? "error") : "error";
                    return (valid, msg, sev);
                }
            }
            catch
            {
                // Fall back to default failure handling if not valid JSON
            }
        }

        return (false, check.Message, "error");
    }

    /// <summary>
    /// Safely evaluate a check condition, catching any resolution exceptions.
    /// Returns <c>null</c> (treated as failed) when evaluation throws.
    /// </summary>
    private static string? EvaluateCondition(CheckRule check, IRenderContext ctx)
    {
        try
        {
            return ctx is RenderContext rc ? rc.ResolveCheck(check.Condition) : ctx.Resolve(check.Condition);
        }
        catch (Exception ex)
        {
            if (ctx.Logger is not null)
            {
                CheckLog.ConditionFailed(ctx.Logger, ex);
            }

            return null;
        }
    }
}

internal static partial class CheckLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Failed to evaluate check condition")]
    public static partial void ConditionFailed(ILogger logger, Exception exception);
}
