using System.Text.Json;

namespace Blazejewicz.A2Ui.Avalonia.Catalog.Entries;

/// <summary>
/// Shared helper for input catalog entries that need two-way data model binding.
/// Centralizes the write-back + fire-action pattern used by TextField, DateTimeInput,
/// CheckBox, Slider, and ChoicePicker.
/// </summary>
internal static class InputHelper
{
    /// <summary>
    /// Write a string value back to the data model (if bound) and fire the user action event.
    /// </summary>
    internal static void NotifyValueChanged(
        IRenderContext ctx,
        string? bindingPath,
        string? value,
        string? componentId = null
    )
    {
        if (bindingPath is not null)
        {
            ctx.UpdateDataModel(bindingPath, value);
        }

        ctx.FireUserAction(InputEvents.ValueChanged, value, componentId);
    }

    /// <summary>
    /// Write a typed JSON value back to the data model (if bound) and fire the user action event with <paramref name="actionPayload"/>.
    /// </summary>
    internal static void NotifyValueChanged(
        IRenderContext ctx,
        string? bindingPath,
        JsonElement? jsonValue,
        object? actionPayload,
        string? componentId = null
    )
    {
        if (bindingPath is not null)
        {
            ctx.UpdateDataModel(bindingPath, jsonValue);
        }

        ctx.FireUserAction(InputEvents.ValueChanged, actionPayload, componentId);
    }
}
