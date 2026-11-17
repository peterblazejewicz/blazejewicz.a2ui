# Package Versioning & Cross-Repository Consumption Policy Research (`Blazejewicz.A2Ui`)

**Date:** 2026-11-17  
**Repository:** `Blazejewicz.A2Ui` (`D:\develop\blazejewicz.a2ui`)  
**Target Packages:** `Blazejewicz.AgUi.Protocol`, `Blazejewicz.A2Ui.Core`, `Blazejewicz.A2Ui.Avalonia`

---

## 1. Current State in `Blazejewicz.A2Ui` & Sibling Repository Conventions

### 1.1 Current Packaging State in `Blazejewicz.A2Ui`

`Blazejewicz.A2Ui` (`A2Ui.slnx`) contains three packable libraries under `src/`, two non-packable desktop sample applications under `samples/`, and four non-packable test projects under `tests/`:

| Project Path | `PackageId` | Target Frameworks | Intra-Repo References & Couplings |
| :--- | :--- | :--- | :--- |
| [`src/A2Ui.Core/A2Ui.Core.csproj`](../src/A2Ui.Core/A2Ui.Core.csproj) | `Blazejewicz.A2Ui.Core` | `net8.0;net10.0` | Grants `<InternalsVisibleTo Include="Blazejewicz.A2Ui.Avalonia" />` ([line 14](../src/A2Ui.Core/A2Ui.Core.csproj#L14)) |
| [`src/AgUi.Protocol/AgUi.Protocol.csproj`](../src/AgUi.Protocol/AgUi.Protocol.csproj) | `Blazejewicz.AgUi.Protocol` | `net8.0;net10.0` | Leaf package (`AGUI.Abstractions` `1.0.1`) |
| [`src/A2Ui.Avalonia/A2Ui.Avalonia.csproj`](../src/A2Ui.Avalonia/A2Ui.Avalonia.csproj) | `Blazejewicz.A2Ui.Avalonia` | `net10.0` | `<ProjectReference>` to `A2Ui.Core` and `AgUi.Protocol` ([lines 25–26](../src/A2Ui.Avalonia/A2Ui.Avalonia.csproj#L25-L26)) |

In the root [`Directory.Build.props`](../Directory.Build.props) and [`Directory.Build.targets`](../Directory.Build.targets):
* `<VersionPrefix>0.2.0</VersionPrefix>` and `<VersionSuffix>dev</VersionSuffix>` ([lines 10–11](../Directory.Build.props#L10-L11)) are set globally for all projects (`0.2.0` reflects the `0.x` minor bump from the `0.1.0` fork repack after the `Blazejewicz.*` namespace/assembly alignment, `FunctionRegistry` relocation to `A2Ui.Core`, and `OneOf` internalization).
* `<Deterministic>true</Deterministic>`, `<PublishRepositoryUrl>true</PublishRepositoryUrl>`, `<EmbedUntrackedSources>true</EmbedUntrackedSources>`, `<IncludeSymbols>true</IncludeSymbols>`, and `<SymbolPackageFormat>snupkg</SymbolPackageFormat>` ([lines 9–23](../Directory.Build.props#L9-L23)) are enabled.
* `<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>`, `<PackageReadmeFile>README.md</PackageReadmeFile>`, and `lock-step:a2ui` in `<PackageTags>` ([lines 15–25](../Directory.Build.props#L15-L25)) are configured.
* `PinRepositoryDependenciesExactly` in [`Directory.Build.targets`](../Directory.Build.targets) pins intra-repository `<ProjectReference>` dependencies to exact `[version]` brackets in generated `.nuspec` files.
* [`tools/pack.sh`](../tools/pack.sh) produces deterministic committed packs (`0.2.0-dev.<count>.g<sha7>`) and cache-busting local inner-loop packs (`0.2.0-local.<count>.g<sha7>.<ms>`).
* Central Package Management (`<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`) is enabled in [`Directory.Packages.props`](../Directory.Packages.props#L3).

---

### 1.2 Conventions Found Across Sibling `.NET` Repositories in `D:\develop`

An inspection of the sibling `.NET 10` repositories in `D:\develop` shows three established versioning and cross-repo consumption patterns:

#### Pattern A: Native MSBuild `VersionPrefix` + Git-Stamped `VersionSuffix` + Vendored Folder Feed (`blazejewicz-voice-internal`, `device-commander`, `gemma-teacher`, `askbox-gemma`)
This is the dominant internal library pattern across the workspace:
* **Producer (`D:\develop\blazejewicz-voice-internal`)**:
  * Uses **zero external versioning packages** (no `MinVer` or `Nerdbank.GitVersioning`).
  * Declares `<VersionPrefix>0.x.y</VersionPrefix>` and `<VersionSuffix>dev</VersionSuffix>`.
  * Groups packages that share `<InternalsVisibleTo>` into a **lock-step train** (`tools/lock-step.manifest`, tagged with `lock-step:<train>` in `<PackageTags>`).
  * Defines a `PinRepositoryDependenciesExactly` target in `Directory.Build.targets` (`AfterTargets="_GetProjectReferenceVersions"`) that wraps `%(ProjectVersion)` in brackets (`[%(ProjectVersion)]`) so intra-repository dependencies emit exact version matches (`version="[0.x.y-...]"`) in `.nuspec`.
  * Ships `tools/pack.sh` with two modes:
    * **Committed mode (`tools/pack.sh`)**: Requires a clean git working tree, calculates commit count (`git rev-list --count HEAD`) and short SHA (`git rev-parse --short=7 HEAD`), and runs `dotnet pack -c Release -p:ContinuousIntegrationBuild=true -p:VersionSuffix="dev.$COUNT.g$SHORT"` into `artifacts/packages/`.
    * **Local inner-loop mode (`tools/pack.sh --local`)**: Accepts a dirty working tree, appends Unix epoch milliseconds (`local.$COUNT.g$SHORT.$(date +%s%3N)`), and packs into `artifacts/local-feed/`, printing `<id> <version>` lines to stdout.
* **Consumers (`D:\develop\device-commander`, `D:\develop\gemma-teacher`, `D:\develop\askbox-gemma`)**:
  * Track a flat `packages/vendor/` folder in git and enforce `<packageSourceMapping>` in `nuget.config` so `Blazejewicz.*` packages resolve **exclusively** from `packages/vendor` (preventing dependency confusion on `nuget.org`), verified by `tools/check-vendored-packages.sh` and `packages.lock.json` (`<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>`).
  * **Existing A2UI consumption in `device-commander`**: `device-commander/Directory.Packages.props` (lines 128–142), `device-commander/tools/vendor-a2ui.sh`, and `device-commander/docs/adr/0019-pluggable-ui-surface-a2ui-cards-and-eventual-consistency.md` **already consume the three A2UI libraries as the `lock-step:a2ui` train** (`Blazejewicz.AgUi.Protocol`, `Blazejewicz.A2Ui.Core`, `Blazejewicz.A2Ui.Avalonia`).
    * Aligning [`src/AgUi.Protocol/AgUi.Protocol.csproj`](../src/AgUi.Protocol/AgUi.Protocol.csproj#L5-L7) to `<PackageId>Blazejewicz.AgUi.Protocol</PackageId>` (reflecting that AG-UI is a distinct protocol from A2UI with zero dependency on `A2Ui.Core`) and including `lock-step:a2ui` in `<PackageTags>` makes `blazejewicz.a2ui` a direct drop-in producer for `device-commander/tools/vendor-packages.sh` and `gemma-teacher/tools/use-local-packages.sh`.
  * **Fast inner-loop script (`gemma-teacher/tools/use-local-packages.sh` & `use-vendored-packages.sh`)**: Calls `tools/pack.sh --local` in the producer repo, adds a temporary `local-feed` source to `nuget.config` with exact package ID mappings (which take precedence over `Blazejewicz.*` wildcard mapping), updates `Directory.Packages.props` with the `-local.<count>.g<sha>.<ms>` stamp, and runs `dotnet restore --force-evaluate`. `use-vendored-packages.sh` restores `Directory.Packages.props`, `nuget.config`, and `*packages.lock.json` from `git HEAD` when done.

#### Pattern B: Root Lockstep `VersionPrefix` (`pipecat-client-dotnet`)
* `D:\develop\pipecat-client-dotnet\Directory.Build.props` sets `<VersionPrefix>2.0.0</VersionPrefix>` in one place at the repository root so all `BL.Pipecat.*` packages share a single version number.

#### Pattern C: `MinVer` + Sibling Repo `<ProjectReference>` (`PipecatVoiceKit.Avalonia`)
* `D:\develop\PipecatVoiceKit.Avalonia\Directory.Build.props` uses `MinVer` (`7.0.0`) with `<MinVerTagPrefix>v</MinVerTagPrefix>`, and defines `<PipecatClientSrc>$(MSBuildThisFileDirectory)..\pipecat-client-dotnet\src</PipecatClientSrc>` to reference `BL.Pipecat.Client` and `BL.Pipecat.Audio` directly via `<ProjectReference>` during active cross-repo development. Because each repo has its own root `Directory.Build.props` and `Directory.Packages.props`, MSBuild's upward directory walk stops at the sibling repo's root without leaking analyzer or CPM rules across repos.

---

## 2. Core Versioning Questions

### 2.1 Lockstep (Unified) vs. Independent Versioning

**Recommendation for `Blazejewicz.A2Ui`: Strict Lockstep Versioning (`lock-step:a2ui`).**

Why lockstep wins decisively for `Blazejewicz.A2Ui`:
1. **`<InternalsVisibleTo>` Binary Coupling:** [`src/A2Ui.Core/A2Ui.Core.csproj`](../src/A2Ui.Core/A2Ui.Core.csproj#L14) grants `<InternalsVisibleTo Include="Blazejewicz.A2Ui.Avalonia" />`. As documented in `blazejewicz-voice-internal/AGENTS.md` (§1, §6 "A lock-step train"), any two packages bound by `InternalsVisibleTo` share internal ABI contracts that are not protected by public SemVer rules. If a consumer restores `Blazejewicz.A2Ui.Avalonia` `0.1.0` alongside `Blazejewicz.A2Ui.Core` `0.1.2`, any internal signature change causes a runtime `MissingMethodException` or `TypeLoadException`.
2. **Existing Consumer Contract:** `device-commander` (`tools/vendor-packages.sh` and `tools/check-vendored-packages.sh`) already models A2UI as a single lock-step train (`lock-step:a2ui`) and fails verification if any member of the `lock-step:a2ui` train resolves at a different version.
3. **Cohesive Release Cadence:** All three libraries (`Core`, `AgUi.Protocol`, `Avalonia`) live in the same repository, share the same `Directory.Build.props` and `Directory.Packages.props`, and are verified together by `A2Ui.Avalonia.Tests`. Keeping a single `<VersionPrefix>` in the root [`Directory.Build.props`](../Directory.Build.props#L10) eliminates version matrix bookkeeping.

### 2.2 How `<ProjectReference>` Translates to `.nuspec` Dependency Ranges (and Why Exact Pinning Matters)

In the .NET SDK's built-in `NuGet.Build.Tasks.Pack.targets` (verified in local .NET 10 SDK `10.0.303/NuGet.Build.Tasks.Pack.targets`, lines 313–351):
* During `dotnet pack`, the target `_GetProjectReferenceVersions` invokes `_GetProjectVersion` on each `<ProjectReference>` item and stores its `$(PackageVersion)` in the `ProjectVersion` metadata of `@(_ProjectReferencesWithVersions)`.
* `PackTask` writes that `ProjectVersion` string into the generated `.nuspec` `<dependency id="..." version="..." />` attribute.
* By NuGet's version range specification ([Microsoft Learn: Package versioning — Version ranges](https://learn.microsoft.com/en-us/nuget/concepts/package-versioning#version-ranges)), a bare version string `version="0.1.0"` in `.nuspec` means **`x >= 0.1.0` (minimum version, inclusive)**, *not* an exact match.
* To enforce true lockstep resolution at restore time, adding the `PinRepositoryDependenciesExactly` target in `Directory.Build.targets` rewrites `ProjectVersion` to `[%(ProjectVersion)]` (`x == 0.1.0`, exact match). If a consuming project ever tries to mix versions of `Blazejewicz.A2Ui.Avalonia` and `Blazejewicz.A2Ui.Core`, NuGet immediately flags `NU1608` / `NU1107` during `dotnet restore`.

### 2.3 Decoupling Package SemVer from A2UI Protocol Versions (`v0.9`, `v0.9.1`, `v1.0`)

**Recommendation: Package version follows SemVer 2.0 (`0.MINOR.PATCH` during internal `0.x` evolution) and is decoupled from the A2UI wire protocol version.**

* `Blazejewicz.A2Ui.Core` and `Blazejewicz.A2Ui.Avalonia` simultaneously support **all three** A2UI protocol versions (`v0.9`, `v0.9.1`, and `v1.0`) in the same assembly via `IVersionAdapter` (`V09VersionAdapter`, `V10VersionAdapter`).
* Therefore, the NuGet package version must track the **.NET library's API and behavior evolution**, not the wire protocol number:
  * While the library is pre-`1.0` (`0.x.y`), follow the workspace's `0.x` SemVer rule (`blazejewicz-voice-internal/AGENTS.md` §1):
    * **Breaking public C# API change or `TargetFramework`/dependency bump** $\rightarrow$ increment **`MINOR`** (e.g., `0.1.0` $\rightarrow$ `0.2.0`).
    * **New backwards-compatible feature, bug fix, or rendering fix** $\rightarrow$ increment **`PATCH`** (e.g., `0.1.0` $\rightarrow$ `0.1.1`).
    * **Comment/README-only change** $\rightarrow$ rides the next `-dev.<count>.g<sha>` stamp without raising `VersionPrefix`.

---

## 3. Comparison of 3 Lean Versioning Mechanisms

| Dimension | 1. Native MSBuild (`VersionPrefix` + `VersionSuffix` + `pack.sh`) | 2. `MinVer` (`PrivateAssets="all"`) | 3. `Nerdbank.GitVersioning` (`nbgv` + `version.json`) |
| :--- | :--- | :--- | :--- |
| **How it works** | `<VersionPrefix>` in `Directory.Build.props`; CLI or script passes `-p:VersionSuffix=...` derived from `git rev-list --count HEAD` + `git rev-parse --short HEAD` ([MSBuild Pack targets](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-pack#options)). | MSBuild target package calculates SemVer from the nearest Git tag (`v0.1.0`) + commit height ([MinVer README](https://github.com/adamralph/minver)). | MSBuild target reads `version.json` (`"0.1.{height}"`) and computes git commit height as a Merkle tree hash ([NBGV docs](https://github.com/dotnet/Nerdbank.GitVersioning)). |
| **External NuGet dependencies** | **Zero.** Built into `Microsoft.NET.DefaultAssemblyInfo.targets` and `NuGet.Build.Tasks.Pack.targets`. | 1 build-only package (`MinVer` `7.0.0`). | 1 build-only package (`Nerdbank.GitVersioning`) + optional `nbgv` CLI tool. |
| **Build-time overhead** | **Zero** during normal `dotnet build` / `dotnet test`; git runs once in `pack.sh`. | Runs `git` subprocess on every build unless `MinVerSkip` is set. | Runs LibGit2 / git height walk on every build. |
| **Dirty-tree local inner-loop support** | **First-class:** `pack.sh --local` appends `.$(date +%s%3N)` (`0.1.0-local.42.g74c562f.1763387500123`), guaranteeing a unique version on every local edit without creating git commits or tags. | **Weak for dirty trees:** `MinVer` only increments height on new *commits* or *tags*. Repeated `dotnet pack` on an uncommitted working tree produces the **exact same version**, hitting the NuGet cache trap unless overridden with `MinVerVersionOverride`. | **Weak for dirty trees:** `nbgv` only changes version on new *commits* (and adds a `-g<sha>` suffix). Uncommitted edits on the same `HEAD` produce the **same version** unless overridden. |
| **Git history requirement** | `git rev-list --count HEAD` needs full history (`fetch-depth: 0` in CI) only when packing. | Requires `fetch-depth: 0` AND annotated/lightweight Git tags on every build. | Requires `fetch-depth: 0` on every build. |
| **Fit with sibling repos** | **Exact match** with `blazejewicz-voice-internal`, `device-commander`, `gemma-teacher`, and `askbox-gemma`. | Used in `PipecatVoiceKit.Avalonia`. | Not used in any sibling repo in `D:\develop`. |

### Verdict on Versioning Mechanism
**Mechanism 1 (Native MSBuild `VersionPrefix` + `VersionSuffix` driven by a lean `tools/pack.sh`)** is the clear winner for `Blazejewicz.A2Ui`:
* It requires **zero new NuGet dependencies** in `Directory.Packages.props`.
* It adds **zero overhead** to `dotnet build` and `dotnet test`.
* It handles **both** reproducible committed builds (`0.1.0-dev.<count>.g<sha>`) and uncommitted inner-loop builds (`0.1.0-local.<count>.g<sha>.<ms>`) without requiring you to create git tags or dummy commits while iterating.
* It is already the exact convention expected by `device-commander/tools/vendor-packages.sh` and `gemma-teacher/tools/use-local-packages.sh`.

> [!WARNING]
> **Why SemVer 2.0 Build Metadata (`+g<sha>`) Must Not Be Used for NuGet Identity:**  
> Per [Microsoft Learn: Package versioning — Normalized version numbers](https://learn.microsoft.com/en-us/nuget/concepts/package-versioning#normalized-version-numbers), NuGet **strips SemVer 2.0 build metadata (`+...`)** during `pack` and `restore` version normalization (`1.0.7+r3456` is treated as identical to `1.0.7`). Always place the commit count, short SHA, and local timestamp inside the **pre-release suffix (`-dev.<count>.g<sha>`)**, where NuGet preserves and sorts dot-separated numeric identifiers numerically under SemVer 2.0 rules.

---

## 4. Comparison of Cross-Repository Consumption Workflows & Avoiding the NuGet Global Cache Trap

### 4.1 The NuGet Global Packages Cache Trap (Why Re-Packing `0.1.0` Silently Serves Stale Code)

Per [Microsoft Learn: Managing the global packages, HTTP cache, and temp folders](https://learn.microsoft.com/en-us/nuget/consume-packages/managing-the-global-packages-and-cache-folders) and [Package Source Mapping](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping#setting-default-sources):
* Whenever NuGet restores a package, it first checks the machine-wide **global-packages** folder (`%userprofile%\.nuget\packages` on Windows, `~/.nuget/packages` on Linux/macOS, or `$NUGET_PACKAGES`).
* **If `<id>/<version>` already exists in `global-packages`, NuGet does not consult any configured feed (not even a local folder feed) and does not compare file hashes.**
* Consequently, if you edit `Blazejewicz.A2Ui.Avalonia`, run `dotnet pack` at the same static version `0.1.0`, and overwrite `0.1.0.nupkg` in a local folder feed, any consuming project (`device-commander`, `gemma-teacher`, etc.) will silently compile and run against the **old cached DLL** in `~/.nuget/packages/blazejewicz.a2ui.avalonia/0.1.0/`.

There are two sound ways to defeat this trap:
1. **Unique Pre-Release Stamp per Pack (Preferred for Inner Loop):** Append a monotonically increasing millisecond timestamp in local mode (`0.1.0-local.<count>.g<sha>.<epoch_ms>`). Because the version string never existed in `~/.nuget/packages`, NuGet is forced to read the new `.nupkg` from the local feed every time.
2. **Targeted Cache Eviction on Committed Vendoring:** When vendoring a committed pack (`0.1.0-dev.<count>.g<sha>`), explicitly delete `~/.nuget/packages/<id_lower>/<version_lower>` before running `dotnet restore --force-evaluate` (as `device-commander/tools/vendor-a2ui.sh` lines 170–173 and `gemma-teacher/tools/vendor-packages.sh` lines 196–210 already do).

---

### 4.2 Comparing the 3 Cross-Repo Consumption Workflows

| Workflow | Best Used When | Pros | Cons / Caveats |
| :--- | :--- | :--- | :--- |
| **Workflow A: Committed Vendored Feed (`packages/vendor` + `nuget.config` source mapping)** | **Default / Committed state** in consuming repos (`device-commander`, `gemma-teacher`, `askbox-gemma`). | 100% offline, reproducible, hash-verified by `packages.lock.json`; works identically on Windows workstation, LattePanda Mu, VENTUNO Q (`linux-arm64`), and CI without private feed credentials. | Requires running `vendor-packages.sh` when promoting a committed A2UI change into a consumer. |
| **Workflow B: Local Timestamped Feed (`pack.sh --local` + `use-local-packages.sh`)** | **Pre-commit testing** of an uncommitted `Blazejewicz.A2Ui` change inside a consumer while keeping the consumer's `PackageReference` graph intact. | Tests the **exact packed `.nupkg` artifact** (catching missing packed resources/styles or dependency issues) without requiring a git commit in `Blazejewicz.A2Ui`. | Leaves temporary folders in `~/.nuget/packages/blazejewicz.a2ui.*/0.1.0-local.*` (cleaned up periodically via `rm -rf ~/.nuget/packages/blazejewicz.a2ui.*/*-local.*`). |
| **Workflow C: Conditional Sibling `<ProjectReference>` Override (`UseLocalA2Ui=true`)** | **Rapid edit-and-debug loop** when editing `Blazejewicz.A2Ui` C#/AXAML controls and stepping through them live in a consumer IDE session. | Zero pack/restore step between edits; F5 build in the consumer compiles `Blazejewicz.A2Ui` source directly. | Does not test `.nupkg` composition; requires the sibling checkout at `../blazejewicz.a2ui`. Note: in repos with `check-project-graph.sh` (`blazejewicz-voice-internal`), conditional `ProjectReference` is disallowed, so keep the override in an untracked `Directory.Build.targets.user` or use Workflow B. |

*(Note: A remote private feed like GitHub Packages requires PAT/token setup on every board and machine and breaks the offline reproducibility guarantee of `ADR-0002` in `device-commander`; Workflows A + B + C cover 100% of local and multi-machine needs with zero cloud dependency.)*

---

## 5. Recommended Policy & Concrete MSBuild / CLI Recipe for `Blazejewicz.A2Ui`

### 5.1 Policy Summary

1. **Single Lockstep Version (`lock-step:a2ui`)**:
   * Keep `<VersionPrefix>0.2.0</VersionPrefix>` and `<VersionSuffix>dev</VersionSuffix>` in the root [`Directory.Build.props`](../Directory.Build.props).
   * Include `lock-step:a2ui` in `<PackageTags>` in [`Directory.Build.props`](../Directory.Build.props#L25) so all three packable projects (`Blazejewicz.AgUi.Protocol`, `Blazejewicz.A2Ui.Core`, `Blazejewicz.A2Ui.Avalonia`) carry the tag expected by `device-commander` and `gemma-teacher`.
   * Follow `0.x` SemVer rules: bump `MINOR` (`0.2.0` $\rightarrow$ `0.3.0`) for breaking API changes; bump `PATCH` (`0.2.0` $\rightarrow$ `0.2.1`) for backwards-compatible features or fixes.
2. **Three Stamp Tiers**:
   * **Plain `dotnet pack` (hand build)**: Produces `0.2.0-dev` (never distributed).
   * **Committed pack (`tools/pack.sh`)**: Refuses a dirty working tree; stamps `0.2.0-dev.<count>.g<sha7>` into `artifacts/packages/`.
   * **Local inner-loop pack (`tools/pack.sh --local`)**: Accepts a dirty working tree; stamps `0.2.0-local.<count>.g<sha7>.<unix_ms>` into `artifacts/local-feed/` and prints `<id> <version>` to stdout.
   * **Tagged release (`dotnet pack -p:VersionSuffix=""`)**: Produces `0.2.0` stable.
3. **Exact Intra-Repo Dependency Pinning (`[version]`)**:
   * Root [`Directory.Build.targets`](../Directory.Build.targets) defines `PinRepositoryDependenciesExactly` so `Blazejewicz.A2Ui.Avalonia.nuspec` requires the exact `[version]` of `Blazejewicz.A2Ui.Core` and `Blazejewicz.AgUi.Protocol`.

---

### 5.2 Concrete MSBuild Recipe

#### 1. Root `Directory.Build.props` Additions
In [`Directory.Build.props`](../Directory.Build.props), set `<VersionPrefix>0.2.0</VersionPrefix>`, `<VersionSuffix>dev</VersionSuffix>`, `<ContinuousIntegrationBuild>` detection, and the `lock-step:a2ui` tag:

```xml
<PropertyGroup>
  <VersionPrefix>0.2.0</VersionPrefix>
  <VersionSuffix>dev</VersionSuffix>
  <ContinuousIntegrationBuild Condition="'$(GITHUB_ACTIONS)' == 'true' or '$(CI)' == 'true'">true</ContinuousIntegrationBuild>
  <PackageTags>a2ui;agent-to-ui;ag-ui;a2a;avalonia;llm;declarative-ui;json-pointer;lock-step:a2ui</PackageTags>
</PropertyGroup>
```

#### 2. Root `Directory.Build.targets`
[`Directory.Build.targets`](../Directory.Build.targets) at the repository root pins intra-repository `<ProjectReference>` dependencies to their exact `[version]` in generated `.nuspec` files:

```xml
<Project>
  <Target Name="PinRepositoryDependenciesExactly"
          AfterTargets="_GetProjectReferenceVersions"
          Condition="'@(_ProjectReferencesWithVersions)' != ''">
    <ItemGroup>
      <_ProjectReferencesWithVersions>
        <ProjectVersion>[%(ProjectVersion)]</ProjectVersion>
      </_ProjectReferencesWithVersions>
    </ItemGroup>
  </Target>
</Project>
```

---

### 5.3 Concrete `tools/pack.sh` Recipe for `Blazejewicz.A2Ui`

[`tools/pack.sh`](../tools/pack.sh) is compatible with `device-commander/tools/vendor-packages.sh` and `gemma-teacher/tools/use-local-packages.sh`:
* Packs `src/AgUi.Protocol/AgUi.Protocol.csproj` (`Blazejewicz.AgUi.Protocol`), `src/A2Ui.Core/A2Ui.Core.csproj` (`Blazejewicz.A2Ui.Core`), and `src/A2Ui.Avalonia/A2Ui.Avalonia.csproj` (`Blazejewicz.A2Ui.Avalonia`) in dependency order.
* Verifies that all three `.nupkg` files exist, carry `<version>$VERSION</version>` and `lock-step:a2ui` in `<tags>`, and pin intra-repository dependencies to `[$VERSION]`.

---

### 5.4 Consuming `Blazejewicz.A2Ui` from Other Local Git Repositories

#### Mode 1: Testing Uncommitted Edits via Local Folder Feed (`--local`)
1. In `Blazejewicz.A2Ui`, pack your uncommitted working tree:
   ```bash
   bash tools/pack.sh --local
   # Outputs to artifacts/local-feed/:
   # Blazejewicz.AgUi.Protocol 0.2.0-local.45.g8a1b2c3.1763387500123
   # Blazejewicz.A2Ui.Core 0.2.0-local.45.g8a1b2c3.1763387500123
   # Blazejewicz.A2Ui.Avalonia 0.2.0-local.45.g8a1b2c3.1763387500123
   ```
2. In the consuming repository, run `tools/use-local-packages.sh ../blazejewicz.a2ui` (as in `gemma-teacher`), which adds `../blazejewicz.a2ui/artifacts/local-feed` to `nuget.config`, updates the three `<PackageVersion>` pins in `Directory.Packages.props`, and runs `dotnet restore --force-evaluate`. Because the millisecond suffix is brand-new, NuGet never serves a stale cached package.

#### Mode 2: Promoting Committed Releases to a Consumer's `packages/vendor` Feed
1. Commit and verify changes in `Blazejewicz.A2Ui`, then in the consuming repository (`device-commander`, `gemma-teacher`), run:
   ```bash
   bash tools/vendor-packages.sh ../blazejewicz.a2ui
   ```

#### Mode 3: Optional Zero-Pack Edit-and-Debug Loop via `UseLocalA2Ui` MSBuild Switch
For consuming repositories that do not forbid conditional references via `check-project-graph.sh` (or via an untracked `Directory.Build.targets.user` file in the consuming repo), you can swap `PackageReference` for `ProjectReference` on the fly with `-p:UseLocalA2Ui=true`:

```xml
<!-- In the consuming repo's Directory.Build.targets (or untracked Directory.Build.targets.user) -->
<Project>
  <PropertyGroup>
    <LocalA2UiRoot Condition="'$(LocalA2UiRoot)' == ''">$(MSBuildThisFileDirectory)..\blazejewicz.a2ui\src</LocalA2UiRoot>
  </PropertyGroup>

  <ItemGroup Condition="'$(UseLocalA2Ui)' == 'true'">
    <PackageReference Remove="Blazejewicz.A2Ui.Core" />
    <PackageReference Remove="Blazejewicz.AgUi.Protocol" />
    <PackageReference Remove="Blazejewicz.A2Ui.Avalonia" />

    <ProjectReference Include="$(LocalA2UiRoot)\A2Ui.Core\A2Ui.Core.csproj" />
    <ProjectReference Include="$(LocalA2UiRoot)\AgUi.Protocol\AgUi.Protocol.csproj" />
    <ProjectReference Include="$(LocalA2UiRoot)\A2Ui.Avalonia\A2Ui.Avalonia.csproj" />
  </ItemGroup>
</Project>
```

---

## 6. Primary Source Citations

1. **Microsoft Learn — NuGet Package Versioning & SemVer 2.0 Rules (Pre-release sorting, version ranges `[x]` vs `x`, and `+metadata` normalization):**  
   <https://learn.microsoft.com/en-us/nuget/concepts/package-versioning>
2. **Microsoft Learn — `dotnet pack` CLI & `VersionPrefix` / `VersionSuffix` / `Version` Precedence Table:**  
   <https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-pack#options>
3. **Microsoft Learn — NuGet Pack and Restore as MSBuild Targets (`PackageVersion`, `VersionPrefix`, `VersionSuffix`, `IsPackable`):**  
   <https://learn.microsoft.com/en-us/nuget/reference/msbuild-targets#pack-target>
4. **Microsoft Learn — Managing the Global Packages, HTTP Cache, and Temp Folders (`global-packages` lookup behavior and cache clearing):**  
   <https://learn.microsoft.com/en-us/nuget/consume-packages/managing-the-global-packages-and-cache-folders>
5. **Microsoft Learn — Package Source Mapping (Precedence rules, wildcard vs. exact ID patterns, and global packages cache interaction):**  
   <https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping>
6. **Microsoft Learn — Central Package Management (CPM) (`Directory.Packages.props` and `ManagePackageVersionsCentrally`):**  
   <https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management>
7. **First-Party .NET 10 SDK MSBuild Targets (Installed SDK `10.0.303`):**
   * `C:\Program Files\dotnet\sdk\10.0.303\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.DefaultAssemblyInfo.targets` (lines 14–18: `VersionPrefix` and `VersionSuffix` composition into `Version`).
   * `C:\Program Files\dotnet\sdk\10.0.303\NuGet.Build.Tasks.Pack.targets` (lines 29–30, 212–284, 313–351: `_GetProjectReferenceVersions`, `_GetProjectVersion`, and `GenerateNuspec`).
8. **MinVer Official Documentation (Tag-driven SemVer calculation and MSBuild properties):**  
   <https://github.com/adamralph/minver#readme>
9. **Nerdbank.GitVersioning Official Documentation (`version.json` and commit-height versioning):**  
   <https://github.com/dotnet/Nerdbank.GitVersioning/blob/main/doc/versions.md>
10. **Local Sibling Repository Sources (`D:\develop`):**
    * `D:\develop\blazejewicz-voice-internal\AGENTS.md` (§1 "The packages", §5 "Packages and the build", §6 "The folder feed" & "A lock-step train")
    * `D:\develop\blazejewicz-voice-internal\Directory.Build.targets` (`PinRepositoryDependenciesExactly` target)
    * `D:\develop\blazejewicz-voice-internal\tools\pack.sh` and `tools/check-lock-step.sh`
    * `D:\develop\device-commander\Directory.Packages.props` (lines 128–142: `lock-step:a2ui` pins)
    * `D:\develop\device-commander\tools\vendor-a2ui.sh`, `tools/vendor-packages.sh`, and `tools/check-vendored-packages.sh`
    * `D:\develop\device-commander\docs\adr\0002-net10-core-on-blazejewicz-packages.md` and `docs/adr/0019-pluggable-ui-surface-a2ui-cards-and-eventual-consistency.md`
    * `D:\develop\gemma-teacher\tools\use-local-packages.sh`, `tools/use-vendored-packages.sh`, and `tools/vendor-packages.sh`
    * `D:\develop\pipecat-client-dotnet\Directory.Build.props` and `D:\develop\PipecatVoiceKit.Avalonia\Directory.Build.props`
