---
name: review-spec
description: Cross-reference A2UI protocol specifications with .NET Core SDK, Avalonia renderer, and Shell implementations for a given topic.
---

Cross-reference these implementation sources for the topic the user specifies:

1. **Specification**: `specification/v0_9_1/docs/a2ui_protocol.md`, `specification/v1_0/docs/a2ui_protocol.md`, relevant JSON schemas in `specification/v0_9_1/json/` and `specification/v1_0/json/`, `catalogs/basic/v1/`, and `conformance/core/`
2. **Core SDK**: `src/A2Ui.Core/` — focus on message models, version adapters, data model, and validation
3. **Avalonia renderer**: `src/A2Ui.Avalonia/` — focus on catalog controls, function registry, and event bridge
4. **Desktop shell**: `samples/A2Ui.Avalonia.Shell/` — focus on A2A client integration and message handling

For the requested topic:
* Read the relevant sections across the specification and implementation sources
* Report any discrepancies (naming, format, version differences across `v0.9`, `v0.9.1`, and `v1.0`)
* Flag any known constraints or protocol gotchas

Output a comparison table when useful.
