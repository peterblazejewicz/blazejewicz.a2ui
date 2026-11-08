# A2UI Conformance Suite (`core`)

This directory contains a read-only subset of the language-agnostic A2UI conformance suite sourced from upstream [`google/A2UI/conformance`](https://github.com/google/A2UI/tree/main/conformance):

* `conformance_schema.json`: JSON Schema defining the structure of declarative conformance test suites.
* `core/`: Core protocol conformance fixtures and YAML test cases (`validator_v0_9.yaml`, `validator_v1_0.yaml`, `data_model.yaml`, `expressions.yaml`, `functions.yaml`, `rpc_functions.yaml`, `error_responses.yaml`, `accessibility.yaml`, `node_resolution.yaml`, `agent_function_routing.yaml`, and `test_data/`).

## Purpose in This Repository

* Serves as the authoritative behavioral reference for `Blazejewicz.A2Ui.Core` and `Blazejewicz.A2Ui.Avalonia` validation, data binding, expression evaluation, and RPC conformance.
* Synchronized from upstream via [`.agents/skills/sync-upstream-specs/SKILL.md`](../.agents/skills/sync-upstream-specs/SKILL.md). Do not modify `conformance_schema.json` or files under `core/` directly.
