# .NET A2UI SDK and Avalonia renderer release notes

## Release summary

* Target runtime: .NET 8 and .NET 10 (`net8.0;net10.0`) for core SDK packages; .NET 10 (`net10.0`) for the Avalonia `12.1.5` renderer and desktop applications, with C# 14 (`<LangVersion>14</LangVersion>`)
* UI framework: Avalonia `12.1.5` (`Avalonia.Controls.DataGrid` `12.1.2`, `Avalonia.DiagnosticsSupport` `2.2.3`)
* Supported A2UI protocol versions: `v1.0`, `v0.9.1`, and `v0.9`
* External protocol dependencies: `AGUI.Abstractions` (`1.0.1`) and `A2A.V0_3` (`1.0.0-preview4`)
* Test suites: `AgUi.Protocol.Tests`, `A2Ui.Core.Tests`, and `A2Ui.Avalonia.Tests` running on the Microsoft Testing Platform (MTP) with xUnit v3

## Included packages and applications

* `Blazejewicz.A2Ui.Core` (`src/A2Ui.Core/`): .NET implementation of the A2UI protocol, state management, data binding, 26 built-in functions (`FunctionRegistry`, `BuiltInFunctions`, `ExpressionParser`), validation, and RPC execution.
* `Blazejewicz.A2Ui.AgUi.Protocol` (`src/AgUi.Protocol/`): Streaming SSE parser (`SseEventParser`) and tool-call delta accumulator (`ToolCallArgsAccumulator`) built on `AGUI.Abstractions` (`1.0.1`).
* `Blazejewicz.A2Ui.Avalonia` (`src/A2Ui.Avalonia/`): Native Avalonia `12.1.5` renderer for A2UI surfaces, providing 18 Basic Catalog controls (`CatalogRegistry`, `Controls/A2UiSurface.cs`) and the `AgentEventBridge`.
* `Blazejewicz.A2Ui.Avalonia.Gallery` (`samples/A2Ui.Avalonia.Gallery/`): Offline desktop gallery loading 79 specification examples across `v0.9` (7 minimal examples), `v0.9.1` (36 basic examples), and `v1.0` (36 Basic Catalog v1 examples).
* `Blazejewicz.A2Ui.Avalonia.Shell` (`samples/A2Ui.Avalonia.Shell/`): Connected desktop client communicating with A2UI agents over A2A JSON-RPC using `A2A.V0_3` (`1.0.0-preview4`).

## Key capabilities in this release

### Multi-version A2UI protocol support (`v0.9`, `v0.9.1`, `v1.0`)

* Version adapter pipeline (`IVersionAdapter`, `V09VersionAdapter`, `V10VersionAdapter`, `VersionAdapterFactory`) normalizes incoming envelopes into `InternalOperation` records while preserving per-surface protocol semantics (`Surface.ProtocolVersion`).
* Full `v1.0` message support (`AgentToRendererMessage`, `RendererToAgentMessage`, `CallAgentFunction`, `CallRendererFunction`, `FunctionResponse`, `RendererAction`, `RendererError`) alongside `v0.9` and `v0.9.1` envelopes (`A2UiMessage`, `ClientToServerMessage`).
* Version-scoped `DynamicValueConverter` supporting `v1.0` `@path`, `@call`, `@index`, `@@` literal escape, and `ObjectValue` dictionaries, while enforcing `v0.9` and `v0.9.1` `path`/`call` rules.
* Built-in function engine in `Blazejewicz.A2Ui.Core.Functions` (`FunctionRegistry`, `BuiltInFunctions`, `ExpressionParser`) providing 26 built-in formatting, arithmetic, comparison, logical, string, validation, and action functions.
* Bidirectional RPC engine (`RpcHandler`, implementing `IDisposable`) with UAX 31 identifier validation (`Uax31Validator`), `AllowedCallers` enforcement (`agentOnly`, `rendererOnly`, `rendererOrAgent`), component binding validation (`ValidateComponentBindings`), outbound listener and disposal guards (`RPC_NO_LISTENER`, `RPC_DISPOSED`), timeout handling, cancellation propagation, and recursion depth limits.
* Portable A2UI regex dialect parser, linear-time static safety analyzer, and translator (`RegexSubset`) enforcing code-point limits (`256` pattern, `4096` value) and rejecting unsafe backtracking or non-portable constructs (`PATTERN_TOO_LONG`, `UNSUPPORTED_SYNTAX`, `NESTED_QUANTIFIER`, `OVERLAPPING_ALTERNATION`, `ADJACENT_OVERLAP`).
* Component tree and catalog validation (`CompositionValidator`) enforcing leaf, single-child, and modal composition rules, duplicate ID detection, and multi-catalog resolution (`Surface.SupportedCatalogIds`).
* RFC 6901 JSON Pointer `DataModel` with `v1.0` null-deletion semantics (`value: null` deletes keys or array elements) and scoped array template resolution.

### Avalonia 12.1.5 renderer (`A2Ui.Avalonia`)

* 18 Basic Catalog components (`Text`, `Image`, `Icon`, `Video`, `AudioPlayer`, `Divider`, `Row`, `Column`, `List`, `Card`, `Tabs`, `Modal`, `Button`, `TextField`, `CheckBox`, `ChoicePicker`, `DateTimeInput`, `Slider`) hosted via `Controls/A2UiSurface.cs`.
* Render-time protocol version scoping in `A2UiRenderer.Render` and `RenderContext.Resolve` so nested function arguments deserialize under the active surface's protocol version.
* Component features across `v0.9`, `v0.9.1`, and `v1.0`: `ListComponent.ListStyle` (`ordered`, `unordered`, `none`), `SliderComponent` optional `min` (defaulting to `0`), `VideoComponent.PosterUrl`, `CheckHelper` dynamic validation messages and severities (`ValidationResult`), and `MediaSecurityHelper` URL scheme allowlist (`https`, `http`, `data:`).
* Modal keyboard/`Button` activation, trigger `checks` gating, nested modal isolation, and lazy `v1.0` content mounting; omitted-property reset on in-place `Update()` across `ApplyAccessibility` and interactive/media controls; cycle detection (`CyclicReferenceDetected`) and zero-layout-footprint exclusion of unresolved, unknown, or cyclic children in `Row`, `Column`, and `List`.
* In-place control updates with focus guards for interactive inputs and two-way data model write-back via `InputHelper`.

### Build, code style, and observability

* Build-time .NET 10 SDK code style enforcement (`EnforceCodeStyleInBuild=true`, `TreatWarningsAsErrors=true`, `IDE0055`, `Microsoft.CodeAnalysis.NetAnalyzers` `10.0.403`, `Roslynator.Analyzers` `4.15.0`).
* Structured `[LoggerMessage]` logging and `System.Diagnostics.ActivitySource` spans across `A2Ui.Core`, `AgUi.Protocol`, `A2Ui.Avalonia`, `A2Ui.Avalonia.Gallery`, and `A2Ui.Avalonia.Shell` (see [`TELEMETRY.md`](TELEMETRY.md)).

### API surface changes

* **Discriminated union internalization (`S-06`):** `OneOf<T1, T2>` and `OneOf<T1, T2, T3>` in `Blazejewicz.A2Ui.Core` (`OneOf.cs`, `OneOf3.cs`) were intentionally changed from `public` to `internal` (and unused legacy `OneOf` helper surface removed) because no public `Blazejewicz.A2Ui.Core` API exposes `OneOf`. Note this as a breaking change if upgrading from an earlier public release that referenced `OneOf` directly.

## Verification commands

```bash
dotnet format A2Ui.slnx --verify-no-changes
dotnet build A2Ui.slnx --configuration Release
dotnet test A2Ui.slnx --configuration Release --no-build
```
