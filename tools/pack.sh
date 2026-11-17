#!/usr/bin/env bash
# Packs the three libraries of src/ (Blazejewicz.AgUi.Protocol, Blazejewicz.A2Ui.Core, and
# Blazejewicz.A2Ui.Avalonia) into NuGet packages as the lock-step:a2ui train, and verifies
# the generated .nuspec metadata.
#
# Committed mode (no flag) writes artifacts/packages and refuses a dirty git working tree.
# The version is <VersionPrefix>-dev.<count>.g<short sha>: <count> (`git rev-list --count HEAD`)
# orders packs monotonically and <short sha> separates branches at the same commit depth.
# Plain <VersionPrefix> is reserved for tagged releases.
#
# Local mode (--local) accepts a dirty working tree and writes artifacts/local-feed (without
# clearing earlier local runs). The version is <VersionPrefix>-local.<count>.g<short sha>.<unix ms>.
# The millisecond suffix guarantees every inner-loop pack is unique so NuGet's global-packages
# cache (~/.nuget/packages) never serves stale bytes. In local mode, stdout emits one line per
# package ("<id> <version>") and all diagnostic output goes to stderr so consumer scripts
# (such as tools/use-local-packages.sh) can parse stdout directly.
#
# Usage:
#   tools/pack.sh [--local] [--out <folder>] [-- <msbuild args>]
#
# Exit:
#   0  All three packages packed and verified (exact stamp, lock-step:a2ui tag, and [stamp]
#      intra-repository dependency ranges).
#   1  Dirty working tree in committed mode, existing local package collision, missing package,
#      version/tag/dependency mismatch in .nuspec, or lock-step prefix disagreement.
#   2  Bad arguments, missing required tool (dotnet, git, unzip, sha256sum), not a git checkout,
#      unlisted packable project in src/, or invalid VersionPrefix.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

PLANNED_PROJECTS=(
    "src/AgUi.Protocol/AgUi.Protocol.csproj"
    "src/A2Ui.Core/A2Ui.Core.csproj"
    "src/A2Ui.Avalonia/A2Ui.Avalonia.csproj"
)
TRAIN="a2ui"

property_of() {
    local value
    if ! value="$(dotnet msbuild "$HERE/$1" -nologo -getProperty:"$2")"; then
        echo "==> 2: dotnet msbuild could not read $2 of $1. NOTHING was packed." >&2
        exit 2
    fi
    printf '%s' "${value//$'\r'/}"
}

epoch_ms() {
    local ms
    ms="$(date +%s%3N 2>/dev/null || true)"
    if [[ "$ms" =~ ^[0-9]+$ ]]; then
        printf '%s' "$ms"
    elif command -v python3 >/dev/null 2>&1; then
        python3 -c 'import time; print(int(time.time() * 1000))'
    else
        printf '%s000' "$(date +%s)"
    fi
}

LOCAL=0
OUT_OVERRIDE=""
MSBUILD_ARGS=()

while [ "$#" -gt 0 ]; do
    arg="$1"
    shift
    case "$arg" in
        --local) LOCAL=1 ;;
        --out)
            if [ "$#" -eq 0 ] || [ -z "$1" ]; then
                echo "==> 2: --out needs a folder. NOTHING was packed." >&2
                exit 2
            fi
            OUT_OVERRIDE="$1"
            shift
            ;;
        --)
            MSBUILD_ARGS=("$@")
            break
            ;;
        -h|--help)
            sed -n '2,26p' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *)
            echo "==> 2: unknown argument '$arg'. Usage: tools/pack.sh [--local] [--out <folder>] [-- <msbuild args>]" >&2
            exit 2
            ;;
    esac
done

# In local mode, stdout is reserved for "<id> <version>" lines (via fd 3); all build and
# verification output goes to stderr.
if [ "$LOCAL" -eq 1 ]; then
    exec 3>&1 1>&2
else
    exec 3>&1
fi

for tool in dotnet git unzip sha256sum; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        echo "==> 2: $tool is not on the PATH. NOTHING was packed."
        exit 2
    fi
done

if ! git -C "$HERE" rev-parse --git-dir >/dev/null 2>&1; then
    echo "==> 2: $HERE is not a git checkout. NOTHING was packed."
    exit 2
fi

if [ -n "$OUT_OVERRIDE" ] && [ "$LOCAL" -eq 0 ] && [ -d "$OUT_OVERRIDE" ] && [ -n "$(ls -A "$OUT_OVERRIDE")" ]; then
    echo "==> 2: --out names a folder that is not empty, and a committed pack clears its output folder."
    echo "    Specify an empty folder. NOTHING was packed."
    exit 2
fi

for proj in "${PLANNED_PROJECTS[@]}"; do
    if [ ! -f "$HERE/$proj" ]; then
        echo "==> 2: planned project $proj does not exist. NOTHING was packed."
        exit 2
    fi
done

# Verify no packable project under src/ is missing from PLANNED_PROJECTS.
for csproj in "$HERE"/src/*/*.csproj; do
    [ -f "$csproj" ] || continue
    rel="${csproj#"$HERE"/}"
    listed=0
    for planned in "${PLANNED_PROJECTS[@]}"; do
        if [ "$planned" = "$rel" ]; then listed=1; break; fi
    done
    if [ "$listed" -eq 0 ]; then
        is_packable="$(property_of "$rel" IsPackable | tr '[:upper:]' '[:lower:]')"
        if [ "$is_packable" != "false" ]; then
            echo "==> 2: $rel is packable and is not in PLANNED_PROJECTS. NOTHING was packed."
            exit 2
        fi
    fi
done

if [ "$LOCAL" -eq 0 ]; then
    DIRTY="$(git -C "$HERE" status --porcelain)"
    if [ -n "$DIRTY" ]; then
        echo "==> 1: the working tree is DIRTY, so a committed pack would name a commit that does"
        echo "    not match the packed bytes. Commit first, discard changes, or use --local."
        git -C "$HERE" status --short | sed 's/^/    /'
        exit 1
    fi
fi

COMMIT="$(git -C "$HERE" rev-parse HEAD)"
COUNT="$(git -C "$HERE" rev-list --count HEAD)"
SHORT="$(git -C "$HERE" rev-parse --short=7 HEAD)"

if [ "$LOCAL" -eq 1 ]; then
    OUT="$HERE/artifacts/local-feed"
    STAMP_TAIL="local.$COUNT.g$SHORT.$(epoch_ms)"
else
    OUT="$HERE/artifacts/packages"
    STAMP_TAIL="dev.$COUNT.g$SHORT"
fi

if [ -n "$OUT_OVERRIDE" ]; then
    mkdir -p "$OUT_OVERRIDE"
    OUT="$(cd "$OUT_OVERRIDE" && pwd)"
fi

IDS=()
VERSIONS=()
PREFIXES=()

for proj in "${PLANNED_PROJECTS[@]}"; do
    prefix="$(property_of "$proj" VersionPrefix)"
    if [[ "$prefix" != 0.* ]] || ! [[ "$prefix" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
        echo "==> 2: $proj evaluates to VersionPrefix '$prefix'; expected 0.<minor>.<patch>. NOTHING was packed."
        exit 2
    fi
    id="$(property_of "$proj" PackageId)"
    if [ -z "$id" ]; then
        echo "==> 2: $proj has an empty PackageId. NOTHING was packed."
        exit 2
    fi
    version="$prefix-$STAMP_TAIL"
    if ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+-(dev|local)\.[0-9]+\.g[0-9a-f]{7,}(\.[0-9]+)?$ ]]; then
        echo "==> 2: computed version '$version' does not match the required stamp format. NOTHING was packed."
        exit 2
    fi
    if [ "$LOCAL" -eq 1 ] && [ -f "$OUT/$id.$version.nupkg" ]; then
        echo "==> 1: ${OUT#"$HERE"/}/$id.$version.nupkg already exists. Run again. NOTHING was packed."
        exit 1
    fi
    IDS+=("$id")
    VERSIONS+=("$version")
    PREFIXES+=("$prefix")
    echo "==> $id: $version"
done

# Verify lockstep VersionPrefix agreement across all train members.
for i in "${!IDS[@]}"; do
    if [ "${PREFIXES[$i]}" != "${PREFIXES[0]}" ]; then
        echo "==> 1: ${IDS[$i]} has VersionPrefix ${PREFIXES[$i]} while ${IDS[0]} has ${PREFIXES[0]}."
        echo "    All members of lock-step:$TRAIN must share one VersionPrefix. NOTHING was packed."
        exit 1
    fi
done

echo "==> packing into ${OUT#"$HERE"/} at commit $COMMIT"
if [ "$LOCAL" -eq 0 ]; then
    rm -rf "$OUT"
fi
mkdir -p "$OUT"

for proj in "${PLANNED_PROJECTS[@]}"; do
    if [ "$LOCAL" -eq 1 ]; then
        dotnet pack "$HERE/$proj" -c Release -p:VersionSuffix="$STAMP_TAIL" \
            ${MSBUILD_ARGS[@]+"${MSBUILD_ARGS[@]}"} -o "$OUT"
    else
        dotnet pack "$HERE/$proj" -c Release -p:ContinuousIntegrationBuild=true \
            -p:VersionSuffix="$STAMP_TAIL" ${MSBUILD_ARGS[@]+"${MSBUILD_ARGS[@]}"} -o "$OUT"
    fi
done

stamp_of() {
    local k
    for k in "${!IDS[@]}"; do
        if [ "${IDS[$k]}" = "$1" ]; then
            printf '%s' "${VERSIONS[$k]}"
            return 0
        fi
    done
    return 0
}

echo
echo "==> verifying packages from commit $COMMIT"
PACKED=0
WRONG=0

for k in "${!IDS[@]}"; do
    id="${IDS[$k]}"
    stamp="${VERSIONS[$k]}"
    pkg="$OUT/$id.$stamp.nupkg"
    if [ ! -f "$pkg" ]; then
        echo "    ** missing package $id.$stamp.nupkg in ${OUT#"$HERE"/}"
        WRONG=$((WRONG + 1))
        continue
    fi
    PACKED=$((PACKED + 1))
    sha="$(sha256sum "$pkg" | awk '{print $1}')"
    printf '    %s\n        sha256 %s\n        commit %s\n' "${pkg#"$HERE"/}" "$sha" "$COMMIT"

    nuspec="$(unzip -p "$pkg" '*.nuspec' | tr -d '\r')"
    version="$(printf '%s\n' "$nuspec" | sed -n 's:.*<version>\(.*\)</version>.*:\1:p' | head -n 1)"
    printf '        nuspec version %s\n' "$version"
    if [ "$version" != "$stamp" ]; then
        echo "        ** nuspec version is '$version', expected '$stamp'"
        WRONG=$((WRONG + 1))
    fi

    printf '        lock-step train %s\n' "$TRAIN"
    if ! printf '%s\n' "$nuspec" | grep '<tags>' | grep -qE "(^|[ ;>])lock-step:$TRAIN([ ;<]|\$)"; then
        echo "        ** nuspec of $id does not carry lock-step:$TRAIN in <tags>"
        WRONG=$((WRONG + 1))
    fi

    while IFS= read -r dependency; do
        [ -n "$dependency" ] || continue
        printf '        %s\n' "$(printf '%s' "$dependency" | sed 's/^[[:space:]]*//')"
        depid="$(printf '%s' "$dependency" | sed -n 's:.*id="\([^"]*\)".*:\1:p')"
        want="$(stamp_of "$depid")"
        if [ -n "$want" ]; then
            if ! grep -qF "version=\"[$want]\"" <<< "$dependency"; then
                echo "        ** dependency on $depid does not pin exact version [$want]"
                WRONG=$((WRONG + 1))
            fi
        fi
    done < <(printf '%s\n' "$nuspec" | grep '<dependency ' || true)
done

if [ "$PACKED" -eq 0 ]; then
    echo "==> 1: dotnet pack produced no packages."
    exit 1
fi
if [ "$WRONG" -ne 0 ]; then
    echo "==> 1: $WRONG verification error(s) above. Do not distribute these packages."
    exit 1
fi

echo "==> 0: $PACKED package(s) packed at $COMMIT with exact lock-step:$TRAIN dependencies."

if [ "$LOCAL" -eq 1 ]; then
    for k in "${!IDS[@]}"; do
        printf '%s %s\n' "${IDS[$k]}" "${VERSIONS[$k]}" >&3
    done
fi
