---
name: build-and-test
description: How to build, test, and format .NET projects in the A2UI repository. Use this when verifying or testing changes.
---

- See `../project-structure/SKILL.md` for project structure details.
- Core SDK and test projects (`A2Ui.Core`, `AgUi.Protocol`, `A2Ui.TestHelpers`, `A2Ui.Core.Tests`, `AgUi.Protocol.Tests`) multi-target `net8.0;net10.0`; Avalonia projects (`A2Ui.Avalonia`, `A2Ui.Avalonia.Tests`, `Gallery`, `Shell`) target `net10.0`.

## Build, Test, and Format Commands

Run all commands from the repo root. Use `--tl:off` when building to avoid terminal flickering in agents.

```bash
# Build all 9 projects
dotnet build A2Ui.slnx --configuration Release --tl:off

# Run all tests (3 test projects, all use Microsoft Testing Platform via xUnit v3)
dotnet test A2Ui.slnx --configuration Release --no-build

# Format all C# files and verify code style / analyzer rules are clean
dotnet format A2Ui.slnx --verify-no-changes

# Format whitespace only across the solution
dotnet format whitespace A2Ui.slnx

# Build/test/format a specific project (preferred for isolated/internal changes)
dotnet build src/A2Ui.Avalonia/A2Ui.Avalonia.csproj --configuration Release --tl:off
dotnet test tests/A2Ui.Avalonia.Tests --configuration Release
dotnet format whitespace src/A2Ui.Avalonia/A2Ui.Avalonia.csproj

# Run a single test using MTP filter-query syntax (MTP flags follow `--`)
# Use /<assembly>/<namespace>/<class>/<method> path with `*` wildcards
dotnet test tests/A2Ui.Core.Tests --configuration Release -- --filter-query "/*/Blazejewicz.A2Ui.Core.Tests/DataModelTests/Apply_UpdateDataModel_SetsNestedPath"

# Run tests matching a pattern across the solution (suppress exit code 8 when a project has no matches)
dotnet test A2Ui.slnx --configuration Release --no-build -- --filter-query "/*/*/DataModelTests/*" --ignore-exit-code 8

# Run tests directly via dotnet run (MTP native command line, bypasses dotnet test)
dotnet run --project tests/A2Ui.Core.Tests --framework net10.0 --configuration Release

# Show MTP command line help for a test project
dotnet run --project tests/A2Ui.Core.Tests --framework net10.0 --configuration Release -- --help

# Run sample desktop applications
dotnet run --project samples/A2Ui.Avalonia.Gallery/A2Ui.Avalonia.Gallery.csproj --configuration Release
dotnet run --project samples/A2Ui.Avalonia.Shell/A2Ui.Avalonia.Shell.csproj --configuration Release
```

## Speeding Up Builds and Testing

| Change type | What to do |
|-------------|------------|
| Isolated/Internal logic | Build only the affected project and its test project. Fix issues, then build the full solution and run all tests before committing. |
| Public API surface | Build the full solution and run all tests immediately. |

### Package Restore tip

`dotnet build` restores packages on each build, which can be slow.
Unless packages have changed, add `--no-restore` to skip this step:

```bash
dotnet build A2Ui.slnx --configuration Release --no-restore --tl:off
```

Remember to run `dotnet restore` after pulling changes, modifying project references, or building for the first time.

## Microsoft Testing Platform (MTP)

Tests use the [Microsoft Testing Platform](https://learn.microsoft.com/dotnet/core/testing/unit-testing-platform-intro) via xUnit v3 (`UseMicrosoftTestingPlatformRunner=true` and `TestingPlatformDotnetTestSupport=true` without a `dotnet.config`, so `dotnet test` uses the VSTest bridge). Key rules when invoking `dotnet test`:

- **`dotnet test A2Ui.slnx`** runs all test projects across the solution; when targeting an individual test project, pass the project path positionally (`dotnet test tests/<Project>`) rather than `--project`, and pass MTP flags (such as `--filter-query`, `--ignore-exit-code`, `--report-xunit-trx`) **after `--`**.
- **Test output** uses the MTP format (e.g., `Passed! - Failed: 0, Passed: 108, Skipped: 0`).
- **Test filtering** uses `--filter-query` with `/<assembly>/<namespace>/<class>/<method>` path syntax and `*` wildcards, not the VSTest `--filter "FullyQualifiedName~..."` syntax.
- **TRX reports** use `--report-xunit-trx` (after `--`) instead of `--logger trx`.
- **Running a test project directly** via `dotnet run --project <test-project>` bypasses `dotnet test` and runs the test executable with the MTP command line directly.
- **Exit code 8** means "no tests matched the filter" -- not a real failure. Use `--ignore-exit-code 8` (after `--`) when running filtered tests across the solution to suppress this.

## Project Map

| Source Project | Test Project | Description |
|----------------|-------------|-------------|
| `src/AgUi.Protocol/` | `tests/AgUi.Protocol.Tests/` | AG-UI event types, SSE parser |
| `src/A2Ui.Core/` | `tests/A2Ui.Core.Tests/` | A2UI messages, adapters, `SurfaceManager`, `DataModel`, `FunctionRegistry` (26 functions), `RpcHandler` |
| `src/A2Ui.Avalonia/` | `tests/A2Ui.Avalonia.Tests/` | Avalonia 18 Basic Catalog controls (`CatalogRegistry`, `Controls/A2UiSurface.cs`), renderer, bridge |
| -- | `tests/A2Ui.TestHelpers/` | Shared test utilities (not runnable) |
| `samples/A2Ui.Avalonia.Gallery/` | `tests/A2Ui.Avalonia.Tests/` (`GalleryParityTests`) | Offline spec-example replay harness (79 examples) |
| `samples/A2Ui.Avalonia.Shell/` | `tests/A2Ui.Avalonia.Tests/` (`ShellParityTests`) | A2A desktop Shell client |
