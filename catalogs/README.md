# A2UI Basic Catalog (`v1.0`)

This directory contains a read-only copy of the official A2UI `v1.0` Basic Catalog (`basic/v1/`) sourced from upstream [`google/A2UI/catalogs/basic/v1`](https://github.com/google/A2UI/tree/main/catalogs/basic/v1):

* `basic/v1/catalog.json` and `basic/v1/common_types.json`: Authoritative `v1.0` Basic Catalog component and function schemas.
* `basic/v1/examples/*.json`: Golden `v1.0` Basic Catalog example payloads.

## Purpose in This Repository

* Linked into `tests/A2Ui.Avalonia.Tests` and `samples/A2Ui.Avalonia.Gallery` (`Specs/v1_0/basic/`) at build time to verify and preview all `v1.0` Basic Catalog components and functions.
* Synchronized from upstream via [`.agents/skills/sync-upstream-specs/SKILL.md`](../.agents/skills/sync-upstream-specs/SKILL.md). Do not modify files under `basic/v1/` directly.
