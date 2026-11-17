# Telemetry and observability (`Blazejewicz.A2Ui`)

## 1. Overview

`Blazejewicz.A2Ui` instruments the core protocol SDK, AG-UI streaming parser, Avalonia renderer, and desktop sample apps (`Shell` and `Gallery`) using standard .NET observability primitives:

* **Distributed tracing:** `System.Diagnostics.ActivitySource` spans across transport, surface state mutations, and UI rendering.
* **Metrics:** `System.Diagnostics.Metrics.Meter` counters and gauges for surface lifecycle, operation throughput, and error rates.
* **Structured logging:** Compile-time source-generated `[LoggerMessage]` partial methods (`Microsoft.Extensions.Logging`) for zero-allocation structured events.
* **Ambient correlation:** `ILogger.BeginScope` key-value dictionaries flowing across `AsyncLocal` boundaries.

Public OpenTelemetry registration constants for the core SDK are exposed on `Blazejewicz.A2Ui.Core.A2UiTelemetry` (`ActivitySourceName` and `MeterName`, both `"A2Ui.Core"`).

## 2. Distributed tracing (`ActivitySource`)

Five `ActivitySource` instances instrument execution across the solution:

| `ActivitySource` Name | Defined In | Span Name(s) | Kind | Key Tags |
|-----------------------|------------|--------------|------|----------|
| `A2Ui.Core` | `src/A2Ui.Core/Diagnostics.cs` | `Surface.CreateSurface`<br/>`Surface.UpdateComponents`<br/>`Surface.UpdateDataModel`<br/>`Surface.DeleteSurface`<br/>`Surface.CallRendererFunction`<br/>`Surface.AgentFunctionResponse` | `Internal` | `a2ui.surface_id`, `a2ui.message_type` |
| `AgUi.Protocol` | `src/AgUi.Protocol/Diagnostics.cs` | `ToolCallArgs.Complete` | `Internal` | `a2ui.tool_call_id`, `a2ui.total_length` |
| `A2Ui.Avalonia.Renderer` | `src/A2Ui.Avalonia/Diagnostics.cs` | `Renderer.Render` | `Internal` | `a2ui.surface_id`, `a2ui.catalog_id` |
| `A2Ui.Avalonia.Bridge` | `src/A2Ui.Avalonia/Diagnostics.cs` | `EventBridge.ProcessLoop`<br/>`EventBridge.DispatchEvent` | `Consumer`<br/>`Internal` | `a2ui.event_type`, `a2ui.event_index` |
| `A2Ui.Shell.A2AClient` | `samples/A2Ui.Avalonia.Shell/Diagnostics.cs` | `A2A.SendMessage` | `Client` | `http.method`, `http.url`, `http.status_code`, `http.response_content_length`, `a2a.correlation_id`, `a2a.message_id`, `a2ui.message_count` |

### Span hierarchy and error semantics

* **Context propagation:** When `A2AAgentClient.SendAsync` starts `A2A.SendMessage`, `Activity.Current` flows automatically to downstream `Surface.<Op>` and `Renderer.Render` child spans.
* **Error status:** Validation failures, transport faults, and unhandled exceptions mark the active span via `Activity.SetStatus(ActivityStatusCode.Error, ex.Message)` and `Activity.AddException(ex)`. Normal cancellation (`OperationCanceledException`) does not set error status.

## 3. Metrics (`System.Diagnostics.Metrics.Meter`)

`Blazejewicz.A2Ui.Core` publishes low-cardinality OpenTelemetry metrics via the `"A2Ui.Core"` meter (`A2UiTelemetry.MeterName`):

| Instrument Name | Type | Unit | Tags | Description |
|-----------------|------|------|------|-------------|
| `a2ui.surface.operations` | `Counter<long>` | `{operation}` | `a2ui.operation.type` (`createSurface`, `updateComponents`, `updateDataModel`, `deleteSurface`, `callRendererFunction`, `agentFunctionResponse`), `a2ui.status` (`ok`, `error`) | Number of A2UI surface and RPC operations processed. |
| `a2ui.surfaces.active` | `UpDownCounter<int>` | `{surface}` | *(none)* | Number of currently active A2UI surfaces (`+1` on surface creation, `-1` on actual deletion, `-N` on `Clear()`). |
| `a2ui.errors` | `Counter<long>` | `{error}` | `a2ui.error.code` (`INTEGRITY_ERROR`, `CATALOG_NOT_FOUND`, `DATA_ERROR`, etc.) | Number of A2UI protocol or operational errors reported. |

### Metric cardinality rule

* High-cardinality identifiers such as `a2ui.surface_id` are attached **only** to `Activity` spans and structured log scopes — **never** to metric tags. Metric dimensions are strictly bounded to protocol operation names, status values, and canonical error codes.

## 4. Structured logging (`[LoggerMessage]`)

All log events are emitted through source-generated `[LoggerMessage]` delegates with stable `EventId` values and structured property templates:

| Logger Class | Category (`ILogger<T>`) | EventIds | Diagnostic Scope |
|--------------|-------------------------|----------|------------------|
| `SurfaceManagerLog` | `Blazejewicz.A2Ui.Core.Surfaces.SurfaceManager` | `1`–`11` | Surface creation/deletion/clearing (`Information`), component/data-model updates and message dispatch (`Debug`), duplicate/unknown/missing-root/validation/data-model warnings (`Warning`). |
| `FunctionLog` | `Blazejewicz.A2Ui.Core.Functions.FunctionRegistry` | `1`–`2` | Unknown function lookup and function evaluation errors (`Warning`). |
| `SseLog` | Caller `ILogger` (`SseEventParser.ParseAsync`) | `1` | Malformed or unknown SSE event line skipped (`Warning`). |
| `ToolCallArgsAccumulatorLog` | `Blazejewicz.AgUi.Protocol.ToolCallArgsAccumulator` | `1`–`4` | Tool-call accumulation start/completion (`Debug`), delta progress (`Trace`), and orphaned incomplete calls (`Warning`). |
| `CatalogRegistryLog` | `Blazejewicz.A2Ui.Avalonia.Catalog.CatalogRegistry` | `1`–`2` | Control registration (`Debug`) and unknown component lookup misses (`Warning`). |
| `CheckLog` | Renderer `ILogger` (`Blazejewicz.A2Ui.Avalonia.A2UiRenderer`) | `1` | Check condition evaluation failure warnings (`Warning`). |
| `MediaLog` | `Blazejewicz.A2Ui.Avalonia.Catalog.Entries.ImageCatalogEntry` | `1`–`2` | Oversized image skips and image download/decode failures (`Warning`). |
| `RendererLog` | `Blazejewicz.A2Ui.Avalonia.A2UiRenderer` | `1`–`15` | Component rendering and template expansion (`Debug`); parent detach, recursion depth, expression/function resolution, data-model write-back, unknown component, cycle detection, and button action failures (`Warning`). |
| `BridgeLog` | `Blazejewicz.A2Ui.Avalonia.AgentEventBridge` | `1`–`9` | Processing loop start/completion (`Information`), event and tool-call dispatch (`Debug`), malformed/invalid message skips (`Warning`), and subscriber, UI dispatch, or stream failures (`Error`). |
| `A2AAgentClientLog` | `Blazejewicz.A2Ui.Avalonia.Shell.Services.A2AAgentClient` | `1`–`7`, `9`–`10` | Request start (`Information`), agent card and message extraction (`Debug`), empty/malformed responses (`Warning`), and transport or agent errors (`Error`). |
| `LoggingHttpMessageHandlerLog` | `Blazejewicz.A2Ui.Avalonia.Shell.Services.LoggingHttpMessageHandler` | `1`–`3` | HTTP request start and completion timing/byte counts (`Debug`) and transport failures (`Warning`). |
| `RequestSummaryLoggerLog` | `Blazejewicz.A2Ui.Avalonia.Shell.Services.RequestSummaryLogger` | `1` | Single-line request completion summary (`Information`) derived from stopped `A2A.SendMessage` activities. |
| `ShellViewModelLog` | `Blazejewicz.A2Ui.Avalonia.Shell.ViewModels.ShellViewModel` | `1`–`10` | Inbound message dispatch (`Debug`), query/action completion counts and action dispatch without user prompt text (`Information`), batch/agent-card warnings (`Warning`), and query/action errors (`Error`). |
| `ShellWindowLog` | `Blazejewicz.A2Ui.Avalonia.Shell.Views.ShellWindow` | `1`–`2` | Shell initialization and user action handling failures (`Error`). |
| `GalleryDataLoaderLog` | `Blazejewicz.A2Ui.Avalonia.Gallery.Services.GalleryDataLoader` | `1`–`3` | Missing specs directory, malformed JSON, and file read warnings (`Warning`). |
| `GalleryViewModelLog` | `Blazejewicz.A2Ui.Avalonia.Gallery.ViewModels.GalleryViewModel` | `1` | Unexpected message processing failures (`Error`). |
| `GalleryWindowLog` | `Blazejewicz.A2Ui.Avalonia.Gallery.Views.GalleryWindow` | `1`–`2` | Missing `SurfaceHost` control warning (`Warning`) and gallery initialization failure (`Error`). |

### Ambient scope correlation (`BeginScope`)

* **Transport scope (`A2AAgentClient.SendAsync`):** Attaches `CorrelationId` (`Guid.NewGuid().ToString("N")`) and `MessageId` to all logs emitted during an agent round-trip.
* **Surface scope (`SurfaceManager.Process`):** Attaches `SurfaceId` and `MessageType` to all logs emitted while processing a message envelope.

## 5. Sensitive data policy

| Log Level | Permitted Payload Content |
|-----------|---------------------------|
| `Information` and above | Structural metadata only (IDs, counts, status codes, durations). No user prompt text, form values, or raw JSON payloads. |
| `Debug` | Truncated previews only (first 200 characters of text queries). |
| `Trace` | Full wire payloads for local diagnostic debugging only. |
| Never | Authentication headers, API keys, and bearer tokens are never logged at any level. |

## 6. Testing telemetry (`Blazejewicz.A2Ui.TestHelpers`)

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
