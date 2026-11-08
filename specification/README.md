# A2UI Protocol Specifications

This directory contains a read-only subset of the official A2UI protocol specifications, JSON schemas, and catalog examples sourced from upstream [`google/A2UI/specification`](https://github.com/google/A2UI/tree/main/specification):

* `versions.json`: Protocol version status index.
* `v0_9/`: A2UI `v0.9` JSON schemas and minimal catalog examples.
* `v0_9_1/`: A2UI `v0.9.1` protocol documentation, JSON schemas, and basic catalog examples.
* `v1_0/`: A2UI `v1.0` protocol documentation, evolution guide, JSON schemas, and A2A extension specification.

## Purpose in This Repository

* Referenced by `Blazejewicz.A2Ui.Core` and `Blazejewicz.A2Ui.Avalonia` as the authoritative protocol contract.
* Linked into `tests/A2Ui.Avalonia.Tests` and `samples/A2Ui.Avalonia.Gallery` (`v0_9` minimal and `v0_9_1` basic examples) at build time for automated headless testing and interactive replay.
* Synchronized from upstream via [`.agents/skills/sync-upstream-specs/SKILL.md`](../.agents/skills/sync-upstream-specs/SKILL.md). Do not modify files under `v0_9/`, `v0_9_1/`, `v1_0/`, or `versions.json` directly.
