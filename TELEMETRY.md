# Telemetry and observability (`Blazejewicz.A2Ui`)

## 1. Overview

`Blazejewicz.A2Ui` instruments the core protocol SDK, AG-UI streaming parser, Avalonia renderer, and A2A desktop shell using standard .NET observability primitives:

* **Distributed tracing:** `System.Diagnostics.ActivitySource` spans across transport, surface state mutations, and UI rendering.
* **Structured logging:** Compile-time source-generated `[LoggerMessage]` partial methods (`Microsoft.Extensions.Logging`) for zero-allocation structured events.
* **Ambient correlation:** `ILogger.BeginScope` key-value dictionaries flowing across `AsyncLocal` boundaries.

## 2. Distributed tracing (`ActivitySource`)

Five `ActivitySource` instances instrument execution across the solution:

| `ActivitySource` Name | Defined In | Span Name(s) | Kind | Key Tags |
|-----------------------|------------|--------------|------|----------|
| `A2Ui.Core` | `src/A2Ui.Core/Diagnostics.cs` | `Surface.CreateSurface`<br/>`Surface.UpdateComponents`<br/>`Surface.UpdateDataModel`<br/>`Surface.DeleteSurface` | `Internal` | `a2ui.surface_id`, `a2ui.message_type` |
| `A2Ui.AgUi.Protocol` | `src/AgUi.Protocol/Diagnostics.cs` | `ToolCallArgs.Complete` | `Internal` | `a2ui.tool_call_id`, `a2ui.total_length` |
| `A2Ui.Avalonia.Renderer` | `src/A2Ui.Avalonia/Diagnostics.cs` | `Renderer.Render` | `Internal` | `a2ui.surface_id`, `a2ui.catalog_id` |
| `A2Ui.Avalonia.Bridge` | `src/A2Ui.Avalonia/Diagnostics.cs` | `EventBridge.ProcessLoop`<br/>`EventBridge.DispatchEvent` | `Consumer`<br/>`Internal` | `a2ui.event_type`, `a2ui.event_index` |
| `A2Ui.Shell.A2AClient` | `samples/A2Ui.Avalonia.Shell/Diagnostics.cs` | `A2A.SendMessage` | `Client` | `http.method`, `http.url`, `http.status_code`, `http.response_content_length`, `a2a.correlation_id`, `a2a.message_id`, `a2ui.message_count` |

### Span hierarchy and error semantics

* **Context propagation:** When `A2AAgentClient.SendAsync` starts `A2A.SendMessage`, `Activity.Current` flows automatically to downstream `Surface.<Op>` and `Renderer.Render` child spans.
* **Error status:** Validation failures, transport faults, and unhandled exceptions mark the active span via `Activity.SetStatus(ActivityStatusCode.Error, ex.Message)` and `Activity.AddException(ex)`. Normal cancellation (`OperationCanceledException`) does not set error status.

## 3. Structured logging (`[LoggerMessage]`)

All log events are emitted through source-generated `[LoggerMessage]` delegates with stable `EventId` values and structured property templates:

| Logger Class | Category (`ILogger<T>`) | EventIds | Diagnostic Scope |
|--------------|-------------------------|----------|------------------|
| `SurfaceManagerLog` | `Blazejewicz.A2Ui.Core.Surfaces.SurfaceManager` | `1`–`11` | Surface creation/deletion (`Information`), component and data-model updates (`Debug`), duplicate/unknown/missing-root/validation warnings (`Warning`). |
| `ToolCallArgsAccumulatorLog` | `Blazejewicz.A2Ui.AgUi.Protocol.ToolCallArgsAccumulator` | `1`–`4` | Streaming tool-call start/delta/end (`Debug`) and unstarted delta warnings (`Warning`). |
| `CatalogRegistryLog` | `Blazejewicz.A2Ui.Avalonia.Catalog.CatalogRegistry` | `1`–`2` | Control registration (`Debug`) and unknown component lookup misses (`Warning`). |
| `RendererLog` | `Blazejewicz.A2Ui.Avalonia.A2UiRenderer` | `8`–`14` | Component rendering and template expansion (`Debug`); recursion depth, missing functions, write-back failures, unknown components, and cycle detection (`Warning`). |
| `BridgeLog` | `Blazejewicz.A2Ui.Avalonia.AgentEventBridge` | `1`–`9` | SSE stream connect/disconnect (`Information`), event queuing and UI dispatch (`Debug`), user action and error forwarding (`Information`/`Error`). |
| `A2AAgentClientLog` | `Blazejewicz.A2Ui.Avalonia.Shell.Services.A2AAgentClient` | `1`–`10` | Request start/completion (`Information`), agent card and message extraction (`Debug`), empty/malformed responses (`Warning`), transport or agent errors (`Error`). |
| `LoggingHttpMessageHandlerLog` | `Blazejewicz.A2Ui.Avalonia.Shell.Services.LoggingHttpMessageHandler` | `1`–`3` | Raw HTTP request/response wire logging (`Debug`) and body capture (`Trace`). |
| `RequestSummaryLoggerLog` | `Blazejewicz.A2Ui.Avalonia.Shell.Services.RequestSummaryLogger` | `1` | Single-line request completion summary (`Information`) derived from stopped `A2A.SendMessage` activities. |
| `ShellViewModelLog` | `Blazejewicz.A2Ui.Avalonia.Shell.ViewModels.ShellViewModel` | `1`–`5` | User query/action submission (`Information`), inbound surface dispatch (`Debug`), and UI error handling (`Error`). |

### Ambient scope correlation (`BeginScope`)

* **Transport scope (`A2AAgentClient.SendAsync`):** Attaches `CorrelationId` (`Guid.NewGuid().ToString("N")`) and `MessageId` to all logs emitted during an agent round-trip.
* **Surface scope (`SurfaceManager.Process`):** Attaches `SurfaceId` and `MessageType` to all logs emitted while processing a message envelope.

## 4. Sensitive data policy

| Log Level | Permitted Payload Content |
|-----------|---------------------------|
| `Information` and above | Structural metadata only (IDs, counts, status codes, durations). No user prompt text, form values, or raw JSON payloads. |
| `Debug` | Truncated previews only (first 200 characters of text queries). |
| `Trace` | Full wire payloads for local diagnostic debugging only. |
| Never | Authentication headers, API keys, and bearer tokens are never logged at any level. |

## 5. Testing telemetry (`Blazejewicz.A2Ui.TestHelpers`)

`Blazejewicz.A2Ui.TestHelpers` provides thread-safe in-memory capture utilities (`TestLoggerProvider` and `TestActivityListener`) for asserting telemetry contracts in unit and integration tests:

```csharp
using var loggerProvider = new TestLoggerProvider();
using var loggerFactory = loggerProvider.CreateFactory();
using var activityListener = new TestActivityListener("A2Ui.Core");

var surfaceManager = new SurfaceManager(loggerFactory);
surfaceManager.Process(createSurfaceMessage);

var log = Assert.Single(loggerProvider.Entries, e => e.EventId.Id == 1);
Assert.Equal("main-surface", log.Properties["SurfaceId"]);

var span = Assert.Single(
    activityListener.StoppedActivities,
    a => a.OperationName == "Surface.CreateSurface" && Equals(a.GetTagItem("a2ui.surface_id"), "main-surface"));
Assert.Equal(ActivityStatusCode.Unset, span.Status);
```
