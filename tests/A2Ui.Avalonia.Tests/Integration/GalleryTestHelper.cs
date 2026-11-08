using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Blazejewicz.A2Ui.Avalonia.Catalog;
using Blazejewicz.A2Ui.Avalonia.Functions;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Surfaces;
using Microsoft.Extensions.Logging;

namespace Blazejewicz.A2Ui.Avalonia.Tests.Integration;

/// <summary>
/// Replays spec JSON examples through SurfaceManager and A2UiRenderer,
/// producing a rendered control tree suitable for headless assertions.
/// </summary>
internal static class GalleryTestHelper
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        AllowOutOfOrderMetadataProperties = true,
    };

    /// <summary>
    /// Load a spec JSON example, process all messages, render, and return the result.
    /// </summary>
    /// <param name="specSubPath">
    /// Path relative to the Specs directory, e.g. "minimal/1_simple_text.json".
    /// </param>
    public static RenderResult ReplayExample(string specSubPath)
    {
        string basePath = Path.Combine(AppContext.BaseDirectory, "Specs", specSubPath);
        if (!File.Exists(basePath))
        {
            throw new FileNotFoundException(
                $"Spec example not found at '{basePath}'. Ensure the file is copied to the test output directory.",
                basePath
            );
        }

        string json = File.ReadAllText(basePath);
        List<A2UiMessage> messages = ParseMessages(json);

        using var loggerFactory = new CapturingLoggerFactory();
        var surfaceManager = new SurfaceManager(loggerFactory);

        // Track the created surface IDs so we can retrieve the last one after replay
        var createdSurfaceIds = new List<string>();
        surfaceManager.SurfaceCreated += (_, args) => createdSurfaceIds.Add(args.Surface.SurfaceId);

        foreach (A2UiMessage message in messages)
        {
            surfaceManager.Process(message);
        }

        string lastSurfaceId = createdSurfaceIds.LastOrDefault()
            ?? throw new InvalidOperationException(
                $"No surface was created after replaying messages from '{specSubPath}'."
            );

        Surface surface =
            surfaceManager.GetSurface(lastSurfaceId)
            ?? throw new InvalidOperationException(
                $"Surface '{lastSurfaceId}' was created but could not be retrieved."
            );

        CatalogRegistry catalog = CatalogRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog, FunctionRegistry.CreateDefault(), loggerFactory);
        var actionLog = new List<UserActionEventArgs>();
        renderer.UserActionFired += (_, args) => actionLog.Add(args);

        Control rootControl = renderer.Render(surface);

        return new RenderResult(
            rootControl,
            surface,
            renderer,
            surfaceManager,
            actionLog,
            loggerFactory.WarningsAndErrors
        );
    }

    /// <summary>
    /// Load a v1.0 spec JSON example, process all messages via VersionAdapter pipeline,
    /// render, and return the result.
    /// </summary>
    public static RenderResult ReplayV10Example(string specSubPath)
    {
        string basePath = Path.Combine(AppContext.BaseDirectory, "Specs", specSubPath);
        if (!File.Exists(basePath))
        {
            throw new FileNotFoundException(
                $"Spec example not found at '{basePath}'. Ensure the file is copied to the test output directory.",
                basePath
            );
        }

        string json = File.ReadAllText(basePath);
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        JsonElement messagesElement = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("messages");

        using var loggerFactory = new CapturingLoggerFactory();
        var surfaceManager = new SurfaceManager(loggerFactory);
        var createdSurfaceIds = new List<string>();
        surfaceManager.SurfaceCreated += (_, args) => createdSurfaceIds.Add(args.Surface.SurfaceId);

        foreach (JsonElement element in messagesElement.EnumerateArray())
        {
            surfaceManager.Process(element);
        }

        string lastSurfaceId = createdSurfaceIds.LastOrDefault()
            ?? throw new InvalidOperationException(
                $"No surface was created after replaying messages from '{specSubPath}'."
            );

        Surface surface =
            surfaceManager.GetSurface(lastSurfaceId)
            ?? throw new InvalidOperationException(
                $"Surface '{lastSurfaceId}' was created but could not be retrieved."
            );

        CatalogRegistry catalog = CatalogRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog, FunctionRegistry.CreateDefault(), loggerFactory);
        var actionLog = new List<UserActionEventArgs>();
        renderer.UserActionFired += (_, args) => actionLog.Add(args);

        Control rootControl = renderer.Render(surface);

        return new RenderResult(
            rootControl,
            surface,
            renderer,
            surfaceManager,
            actionLog,
            loggerFactory.WarningsAndErrors
        );
    }

    /// <summary>
    /// Parse messages from a spec JSON file. Handles both the envelope format
    /// <c>{ "name": ..., "messages": [...] }</c> and a bare array <c>[...]</c>.
    /// </summary>
    private static List<A2UiMessage> ParseMessages(string json)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        JsonElement messagesElement = root.ValueKind == JsonValueKind.Array
            ? root
            : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("messages", out JsonElement msgProp)
                ? msgProp
                : throw new JsonException(
                    "Spec JSON must be either an array of messages or an object with a 'messages' property."
                );

        var messages = new List<A2UiMessage>();
        foreach (JsonElement element in messagesElement.EnumerateArray())
        {
            string rawMessage = element.GetRawText();
            A2UiMessage? message = JsonSerializer.Deserialize<A2UiMessage>(rawMessage, s_jsonOptions);
            if (message is not null)
            {
                messages.Add(message);
            }
        }

        return messages;
    }

    /// <summary>
    /// Find the first control of type <typeparamref name="T"/> in the visual tree.
    /// </summary>
    public static T? FindFirst<T>(Control root)
        where T : Control
    {
        if (root is T match)
        {
            return match;
        }

        foreach (Control child in GetChildren(root))
        {
            T? found = FindFirst<T>(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Collect all controls of type <typeparamref name="T"/> in the visual tree.
    /// </summary>
    public static List<T> FindAll<T>(Control root)
        where T : Control
    {
        var results = new List<T>();
        CollectAll(root, results);
        return results;
    }

    private static void CollectAll<T>(Control current, List<T> results)
        where T : Control
    {
        if (current is T match)
        {
            results.Add(match);
        }

        foreach (Control child in GetChildren(current))
        {
            CollectAll(child, results);
        }
    }

    /// <summary>
    /// Yield child controls from common Avalonia container types.
    /// Handles Panel, ContentControl, Decorator, ScrollViewer, and Popup.
    /// </summary>
    public static IEnumerable<Control> GetChildren(Control parent)
    {
        if (parent is Panel panel)
        {
            foreach (Control child in panel.Children)
            {
                yield return child;
            }
        }
        else if (parent is ScrollViewer scrollViewer)
        {
            if (scrollViewer.Content is Control scrollContent)
            {
                yield return scrollContent;
            }
        }
        else if (parent is ContentControl contentControl)
        {
            if (contentControl.Content is Control content)
            {
                yield return content;
            }
        }
        else if (parent is Decorator decorator)
        {
            if (decorator.Child is Control decoratorChild)
            {
                yield return decoratorChild;
            }
        }
        else if (parent is Popup popup)
        {
            if (popup.Child is Control popupChild)
            {
                yield return popupChild;
            }
        }
    }

    /// <summary>
    /// Simulate a button click by raising the <see cref="Button.ClickEvent"/> routed event.
    /// </summary>
    public static void ClickButton(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
    }

    /// <summary>
    /// Set text on a TextBox, which fires PropertyChanged and triggers two-way binding.
    /// </summary>
    public static void SetText(TextBox textBox, string text)
    {
        textBox.Text = text;
    }
}

/// <summary>
/// Result of replaying a spec example through the rendering pipeline.
/// </summary>
internal sealed record RenderResult(
    Control RootControl,
    Surface Surface,
    A2UiRenderer Renderer,
    SurfaceManager SurfaceManager,
    List<UserActionEventArgs> ActionLog,
    IReadOnlyList<string>? WarningsAndErrors = null
)
{
    /// <summary>
    /// Re-render the surface (e.g. after processing additional messages).
    /// Returns the new root control.
    /// </summary>
    public Control ReRender()
    {
        return this.Renderer.Render(this.Surface);
    }
}

internal sealed class CapturingLoggerFactory : ILoggerFactory
{
    private readonly List<string> _warningsAndErrors = [];

    public IReadOnlyList<string> WarningsAndErrors => this._warningsAndErrors;

    public void AddProvider(ILoggerProvider provider) { }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, this._warningsAndErrors);

    public void Dispose() { }

    private sealed class CapturingLogger(string categoryName, List<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel >= LogLevel.Warning)
            {
                string message = formatter(state, exception);
                string exText = exception is not null ? $" ({exception.GetType().Name}: {exception.Message})" : string.Empty;
                sink.Add($"[{logLevel}] {categoryName}: {message}{exText}");
            }
        }
    }
}
