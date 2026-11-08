---
name: sync-upstream-specs
description: Synchronize upstream A2UI specification, Basic Catalog, and conformance files from google/A2UI into this standalone repository. Use when updating protocol schemas, catalog examples, or conformance test suites from upstream.
---

# Sync Upstream A2UI Specifications

This repository (`Blazejewicz.A2Ui`) is a standalone repository (not a Git fork) that tracks a curated, read-only subset of the upstream [`google/A2UI`](https://github.com/google/A2UI) specification, catalog, and conformance directories.

## Curated Upstream Paths

Only the following 7 upstream paths are tracked and synchronized:

* `specification/versions.json`
* `specification/v0_9`
* `specification/v0_9_1`
* `specification/v1_0`
* `catalogs/basic/v1`
* `conformance/conformance_schema.json`
* `conformance/core`

*(Do not check out deprecated `specification/v0_8`, draft `specification/v0_10`, `catalogs/extended`, or non-.NET `conformance/agent` suites unless explicitly instructed. Top-level `specification/README.md`, `catalogs/README.md`, and `conformance/README.md` are local repository pointers and are preserved across syncs.)*

## Synchronization Routine

Run the following steps from the repository root in PowerShell (`pwsh` or Windows PowerShell):

```powershell
# 1. Ensure working tree is clean before syncing
git status --short

# 2. Blobless shallow fetch of upstream main (downloads commit and tree metadata only)
git fetch --depth=1 --filter=blob:none --no-tags https://github.com/google/A2UI.git main
$upstreamSha = (git rev-parse --short FETCH_HEAD).Trim()

# 3. Define the 7 curated upstream paths
$paths = @(
    "specification/versions.json",
    "specification/v0_9",
    "specification/v0_9_1",
    "specification/v1_0",
    "catalogs/basic/v1",
    "conformance/conformance_schema.json",
    "conformance/core"
)

# 4. Remove existing tracked files first (so upstream deletions/renames propagate),
#    then overlay the fresh files from FETCH_HEAD
git rm -rf --ignore-unmatch --quiet -- $paths
git checkout FETCH_HEAD -- $paths

# 5. Remove FETCH_HEAD and shallow boundary marker, then prune temporary upstream objects from .git
Remove-Item -Force .git/FETCH_HEAD, .git/shallow -ErrorAction SilentlyContinue
git gc --prune=now --quiet

# 6. Inspect changes and run the full .NET build and conformance verification gate
git status --short
dotnet build A2Ui.slnx --configuration Release --tl:off
dotnet test A2Ui.slnx --configuration Release --no-build --tl:off
```

## Post-Sync Verification and Commit

1. **If `git status --short` is empty:** The local specification, catalog, and conformance files are already 100% up to date with upstream `main`.
2. **If files changed and `dotnet test` passes:** Commit the updated specification files recording the upstream commit SHA in the commit message:
   ```bash
   git add specification/ catalogs/ conformance/
   git commit -m "chore(spec): sync upstream google/A2UI@$upstreamSha"
   ```
3. **If `dotnet test` fails after syncing:** Inspect `git diff -- specification/ catalogs/ conformance/` to identify schema, catalog, or conformance rule changes, update the corresponding implementation in `src/A2Ui.Core/` or `src/A2Ui.Avalonia/`, re-run `dotnet test A2Ui.slnx --configuration Release`, and commit.
