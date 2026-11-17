# A2UI agent source of truth (AGENTS.md)

This document is the authoritative guide for AI assistants and contributors working within the `Blazejewicz.A2Ui` repository. It outlines architecture, repository structure, and .NET engineering standards.

## 1. What is A2UI?

A2UI (Agent-to-User Interface) is an open, declarative, streaming-first protocol that allows Large Language Models (LLMs) and autonomous agents to generate and update user interfaces.

Key capabilities:

* Streaming UI: Progressive rendering of components and values to minimize perceived latency.
* Two-way data binding: Synchronization between client data models and agent payloads via RFC 6901 JSON Pointers.
* Local function evaluation: Client-side evaluation of registered functions and validation rules.
* Remote procedure calls: Bidirectional agent-to-renderer and renderer-to-agent function invocation.

## 2. Protocol versioning and authority

The repository supports three versions of the A2UI protocol:

* `v0.9`: Baseline JSON specification with single-operation envelopes, `path`/`call` binding objects, `AccessibilityAttributes`, and `Checkable` validation rules.
* `v0.9.1`: Refines `v0.9` by standardizing the `application/a2ui+json` MIME type, relaxing `surfaceId` lifetime uniqueness so surfaces can be recreated after deletion, and removing `List.listStyle` from the Basic Catalog.
* `v1.0` (`"version": "1.0"` on the wire): Typed `AgentToRendererMessage` / `RendererToAgentMessage` envelopes, `@path` / `@call` / `@index` string expressions, `ObjectValue` dictionaries, bidirectional RPC (`CallAgentFunction`, `CallRendererFunction`), and UAX 31 identifier validation.

Authority rule:
* Base implementation logic on formal schemas and catalogs in `specification/v0_9_1/`, `specification/v1_0/`, and `catalogs/basic/v1/`.
* Files under `specification/`, `catalogs/basic/v1/`, and `conformance/core/` are read-only upstream references and must not be modified directly.
* The `Blazejewicz.A2Ui.Core` engine handles version scoping via `IVersionAdapter`, normalizing messages into unified surface state.

## 3. Codebase and repository structure

The repository is structured around the .NET 10 SDK and native Avalonia desktop renderer:

* `src/`: Library packages (`lock-step:a2ui` train).
  * `A2Ui.Core/` (`Blazejewicz.A2Ui.Core`): Core protocol DTOs, version adapters, `SurfaceManager`, RFC 6901 `DataModel`, `DynamicValue` evaluation, 26 built-in functions (`FunctionRegistry`, `BuiltInFunctions`, `ExpressionParser`), `RpcHandler`, and validation engines (`CompositionValidator`, `Uax31Validator`, `RegexSubset`) (`net8.0;net10.0`).
  * `AgUi.Protocol/` (`Blazejewicz.AgUi.Protocol`): Streaming SSE parser (`SseEventParser`) and tool-call accumulator (`ToolCallArgsAccumulator`) built on `AGUI.Abstractions` (`1.0.1`) (`net8.0;net10.0`).
  * `A2Ui.Avalonia/` (`Blazejewicz.A2Ui.Avalonia`): Native Avalonia `12.1.5` renderer with 18 Basic Catalog controls (`CatalogRegistry`, `Controls/A2UiSurface.cs`), render-time version scoping, media URL security filtering, and event bridge (`AgentEventBridge`) (`net10.0`).
* `tests/`: Automated test projects and shared test utilities.
  * `A2Ui.Core.Tests/`: Unit tests and telemetry scenario tests for core functionality (`net8.0;net10.0`).
  * `AgUi.Protocol.Tests/`: Unit tests for AG-UI streaming and accumulator logic (`net8.0;net10.0`).
  * `A2Ui.Avalonia.Tests/`: Headless Avalonia UI tests and specification integration suites (`net10.0`).
  * `A2Ui.TestHelpers/`: Shared `TestLoggerProvider` and `TestActivityListener` capture utilities (`net8.0;net10.0`).
* `samples/`: Avalonia desktop applications (`net10.0`).
  * `A2Ui.Avalonia.Gallery/`: Offline specification replay gallery with 79 examples across `v0.9`, `v0.9.1`, and `v1.0`.
  * `A2Ui.Avalonia.Shell/`: Connected desktop client communicating with A2A agents over JSON-RPC via `A2A.V0_3` (`1.0.0-preview4`).
* `tools/pack.sh`: Lockstep NuGet packaging script supporting committed (`<VersionPrefix>-dev.<count>.g<sha7>`) and local inner-loop (`--local`, `<VersionPrefix>-local.<count>.g<sha7>.<ms>`) pack modes.
* `README.md`, `TELEMETRY.md`: Architecture overview and observability reference.
* `specification/`: Versioned upstream protocol schemas, catalogs, and documentation (`v0_9/`, `v0_9_1/`, `v1_0/`).
* `catalogs/basic/v1/`: v1.0 Basic Catalog specification and golden example payloads.
* `conformance/core/`: Conformance test corpora and test suites.
* `.agents/skills/`: Project-scoped agent skills (`build-and-test`, `project-structure`, `review-spec`, `sync-upstream-specs`).

## 4. Engineering standards and tooling

All projects in this repository use modern .NET 10 conventions:

* Solution format: Microsoft Solution File (`A2Ui.slnx`).
* Target frameworks: Core SDK and test projects target .NET 8 and .NET 10 (`net8.0;net10.0`); Avalonia renderer and desktop applications target .NET 10 (`net10.0`), all using C# 14 language version.
* Central Package Management (CPM): Dependencies and package versions are managed centrally in `Directory.Packages.props`. Do not declare package versions in individual `.csproj` files.
* Build and packaging settings: Shared build configuration in `Directory.Build.props` enforces `Nullable` reference types, `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`, and lockstep `0.x` versioning (`VersionPrefix` + `VersionSuffix`). `Directory.Build.targets` pins intra-repository `<ProjectReference>` dependencies to exact `[version]` ranges in generated `.nuspec` files.
* Testing platform: Tests run on the Microsoft Testing Platform (MTP) with xUnit v3.
* Static analysis: Managed by Roslyn analyzers (`Microsoft.CodeAnalysis.NetAnalyzers` and `Roslynator.Analyzers`).

Canonical verification commands:

```bash
dotnet format A2Ui.slnx --verify-no-changes
dotnet build A2Ui.slnx --configuration Release
dotnet test A2Ui.slnx --configuration Release --no-build
```

To run individual sample applications:

```bash
dotnet run --project samples/A2Ui.Avalonia.Gallery/A2Ui.Avalonia.Gallery.csproj --configuration Release
dotnet run --project samples/A2Ui.Avalonia.Shell/A2Ui.Avalonia.Shell.csproj --configuration Release
```

## 5. Core guidelines for agents

* Prioritize authoritative specifications: Base protocol models and renderer behaviors on the formal schemas and examples under `specification/` and `catalogs/basic/v1/`.
* Maintain agent-agnostic documentation: Write instructions, documentation, and skill recipes in a vendor-neutral manner under `.agents/skills/` and `.mcp.json`.
* Enforce compiler and analyzer cleanliness: All code changes must build with zero warnings and pass `dotnet format` checks under `TreatWarningsAsErrors=true`.
* Known IDE XAML false positive: JetBrains Rider / ReSharper may report `"Invalid markup extension type: expected X, actual CompiledBinding"` in `.axaml` files that set `x:DataType` (`ShellWindow.axaml`, `GalleryWindow.axaml`). The Avalonia XAML compiler resolves `{CompiledBinding}` at build time; treat `dotnet build A2Ui.slnx --configuration Release` as authoritative.
