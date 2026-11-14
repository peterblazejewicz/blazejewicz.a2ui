# A2UI local gallery — Avalonia desktop

A native Avalonia `12.1.5` desktop application that renders 79 A2UI specification examples across `v0.9`, `v0.9.1`, and `v1.0` using the `Blazejewicz.A2Ui.Avalonia` renderer library.

## Features

* Loads 79 specification JSON examples (7 minimal `v0.9`, 36 basic `v0.9.1`, and 36 `v1.0` Basic Catalog v1 examples)
* Three-pane layout: navigation sidebar, preview with message stepper, and inspector
* Message stepper: step through A2UI messages one at a time or replay all at once
* Live data model inspector (pretty-printed JSON)
* Action log: timestamped record of all user interactions in rendered surfaces
* Purely local and filesystem-based (no agent or network required)

## Build and run

```bash
cd samples/A2Ui.Avalonia.Gallery

# Build
dotnet build --configuration Release

# Run (requires display)
dotnet run --configuration Release
```

## Architecture

* MVVM pattern using CommunityToolkit.Mvvm source generators
* Dependency injection via Microsoft.Extensions.DependencyInjection
* GalleryDataLoader reads specification JSON from the `Specs/` directory at runtime
* GalleryViewModel feeds A2UI messages to `SurfaceManager.Process()`
* GalleryWindow code-behind handles `A2UiSurface.Refresh()` and `UserActionFired`
* Application styling via `Application.Resources` (dark theme)
* Renderer styling via `A2UiDefaultStyles.axaml` (loaded by `A2UiSurface`)

## Adding custom examples

Place any valid A2UI JSON file into `bin/Release/net10.0/Specs/minimal/`, `bin/Release/net10.0/Specs/basic/`, or `bin/Release/net10.0/Specs/v1_0/basic/` — the application loads them on next launch without recompilation. JSON format: `{ "name": "...", "description": "...", "messages": [...] }`.
