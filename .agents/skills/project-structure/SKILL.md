---
name: project-structure
description: Explains the project structure of the A2UI .NET/Avalonia solution. Use when navigating or exploring the codebase.
---

# A2UI .NET/Avalonia Project Structure

```
A2UI/                               <- repo root
+-- A2Ui.slnx                       <- solution file (9 projects)
+-- Directory.Build.props            <- shared MSBuild (Nullable, TreatWarningsAsErrors)
+-- Directory.Packages.props         <- central package management
+-- global.json                      <- .NET SDK pin (10.0.x)
+-- AGENTS.md                        <- authoritative repository guide
+-- README.md                        <- architecture, catalog overview, and quickstart
+-- RELEASE_NOTES.md                 <- release summary and capabilities
+-- TELEMETRY.md                     <- ActivitySource and LoggerMessage observability guide
+-- .mcp.json                        <- project-scoped MCP server definitions
+-- .agents/skills/                  <- project-scoped agent skills
|
+-- src/                             <- Library projects
|   +-- A2Ui.Core/                   <- A2UI messages, validation, SurfaceManager, DataModel, FunctionRegistry (26 functions), RpcHandler
|   +-- AgUi.Protocol/               <- AG-UI event types, SSE parser, tool-call accumulator
|   +-- A2Ui.Avalonia/               <- Catalog registry (18 entries), renderer (Controls/A2UiSurface.cs), event bridge
|
+-- tests/                           <- Test projects and shared test utilities
|   +-- A2Ui.Core.Tests/             <- Core unit + telemetry tests
|   +-- AgUi.Protocol.Tests/         <- Protocol unit tests
|   +-- A2Ui.Avalonia.Tests/         <- Headless UI tests + spec example integration tests
|   +-- A2Ui.TestHelpers/            <- Shared TestLoggerProvider + TestActivityListener
|
+-- samples/                         <- Avalonia sample apps
|   +-- A2Ui.Avalonia.Gallery/       <- Offline spec-example replay harness
|   +-- A2Ui.Avalonia.Shell/         <- A2A desktop client
|
+-- specification/                   <- Protocol specs (read-only reference)
|   +-- v0_9/                        <- v0.9 specification
|   +-- v0_9_1/                      <- v0.9.1 specification
|   +-- v1_0/                        <- v1.0 specification
+-- catalogs/basic/v1/               <- v1.0 Basic Catalog specification and examples
+-- conformance/core/                <- Core conformance test suites
```

## Main Folders

| Folder | Contents |
|--------|----------|
| `src/` | Library source projects (`A2Ui.Core` -> `Blazejewicz.A2Ui.Core`, `AgUi.Protocol` -> `Blazejewicz.A2Ui.AgUi.Protocol`, `A2Ui.Avalonia` -> `Blazejewicz.A2Ui.Avalonia`) |
| `tests/` | Unit/headless test projects (`A2Ui.Core.Tests`, `AgUi.Protocol.Tests`, `A2Ui.Avalonia.Tests`) and `A2Ui.TestHelpers` |
| `samples/` | Sample Avalonia desktop applications (`A2Ui.Avalonia.Gallery`, `A2Ui.Avalonia.Shell`) |
| `specification/` | Authoritative protocol specs, JSON schemas, catalog examples (`v0_9`, `v0_9_1`, `v1_0`) |
| `catalogs/basic/v1/` | Authoritative v1.0 Basic Catalog schemas and golden examples |
| `conformance/core/` | Core protocol conformance test fixtures |
