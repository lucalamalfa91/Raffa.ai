#!/usr/bin/env python3
"""fan_out.merge_verify — reject a barrier resolution that still has markers.

Runs in the integration worktree (product clone root). Must not require
MERGE_HEAD to be absent (the auto pass verifies before it commits).

Exit 0: no leftover conflict markers in tracked text files, and — when the
        merge touched backend C# sources — `dotnet build backend/Raffa.slnx`
        succeeds.
Exit 1: a tracked file still contains git conflict marker lines, or the build
        gate failed / timed out.

The build gate exists because a marker scan accepted the e13 phase-3 union of
two whole `MarketEndpointExtensions.cs` files (18 compile errors reached CI).
It runs only when a conflicted or staged path is a C# source under backend/,
and can be skipped with MERGE_VERIFY_SKIP_BUILD=1 (never in an overnight wave).
"""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path

# Helix's _MERGE_VERIFY_TIMEOUT_S is 600 s; leave headroom for the marker scan.
BUILD_TIMEOUT_S = 480
BUILD_SUFFIXES = (".cs", ".csproj", ".slnx", ".props", ".targets")

# Built at runtime so this script does not trip its own scan.
MARKERS = (chr(60) * 7, chr(62) * 7)

# Process files that may legitimately contain marker-like strings (documentation
# examples, this script's own source).  Product paths (backend/, web/, mobile/,
# infra/, .github/, scripts/) are always scanned.
SKIP_PREFIXES = (
    ".helix/agents/",
    ".helix/scripts/",
    ".helix/skills/",
    ".helix/docs/",
)


def tracked_files(repo_root: Path) -> list[str]:
    out = subprocess.check_output(
        ["git", "ls-files", "-z"],
        cwd=repo_root,
        text=True,
    )
    return [p for p in out.split("\0") if p]


def _is_excluded(rel: str) -> bool:
    """True for process files that are allowed to contain marker-like strings."""
    # Normalise backslashes (Windows) to forward slashes for prefix matching.
    norm = rel.replace("\\", "/")
    if any(norm.startswith(p) for p in SKIP_PREFIXES):
        return True
    # Engine-side env var: additional glob prefixes (newline-separated).
    extra = os.environ.get("MERGE_VERIFY_EXCLUDE", "")
    if extra:
        for prefix in extra.splitlines():
            prefix = prefix.strip()
            if prefix and norm.startswith(prefix):
                return True
    return False


def files_with_markers(repo_root: Path, paths: list[str]) -> list[str]:
    hit: list[str] = []
    for rel in paths:
        if _is_excluded(rel):
            continue
        path = repo_root / rel
        try:
            text = path.read_text(encoding="utf-8")
        except (OSError, UnicodeDecodeError):
            continue
        if any(marker in text for marker in MARKERS):
            hit.append(rel)
    return hit


def merge_touched_paths(repo_root: Path) -> list[str]:
    """Paths the in-progress merge touched: still-unmerged (the auto pass verifies
    before `git add`), staged (the resolver agent staged them), and unstaged."""
    paths: set[str] = set()
    for args in (
        ["diff", "--name-only", "--diff-filter=U"],
        ["diff", "--name-only", "--cached"],
        ["diff", "--name-only"],
    ):
        try:
            out = subprocess.check_output(["git", *args], cwd=repo_root, text=True)
        except subprocess.CalledProcessError:
            continue
        paths.update(p.strip() for p in out.splitlines() if p.strip())
    return sorted(paths)


def needs_dotnet_build(paths: list[str]) -> bool:
    """True when any touched path is a C# source or project file under backend/."""
    for rel in paths:
        norm = rel.replace("\\", "/")
        if norm.startswith("backend/") and norm.lower().endswith(BUILD_SUFFIXES):
            return True
    return False


def dotnet_build(repo_root: Path) -> int:
    if os.environ.get("MERGE_VERIFY_SKIP_BUILD", "").strip():
        print("merge_verify: MERGE_VERIFY_SKIP_BUILD set; build gate skipped", file=sys.stderr)
        return 0
    dotnet = shutil.which("dotnet")
    solution = repo_root / "backend" / "Raffa.slnx"
    if dotnet is None or not solution.is_file():
        print("merge_verify: dotnet or backend/Raffa.slnx missing; build gate skipped", file=sys.stderr)
        return 0
    env = os.environ.copy()
    # No lingering servers in the integration checkout: they lock bin/obj and
    # break the next worktree reset (the e13 retry failure mode).
    env["MSBUILDDISABLENODEREUSE"] = "1"
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    cmd = [
        dotnet, "build", str(solution), "--nologo", "-v", "q",
        "-p:UseSharedCompilation=false",
    ]
    try:
        proc = subprocess.run(
            cmd, cwd=repo_root, capture_output=True, text=True, timeout=BUILD_TIMEOUT_S, env=env
        )
    except subprocess.TimeoutExpired:
        print(f"merge_verify: dotnet build exceeded {BUILD_TIMEOUT_S}s; rejecting the resolution", file=sys.stderr)
        return 1
    if proc.returncode != 0:
        print("merge_verify: dotnet build failed after the barrier merge:", file=sys.stderr)
        for line in ((proc.stdout or "") + (proc.stderr or "")).splitlines()[-40:]:
            print(f"  {line}", file=sys.stderr)
        return 1
    print("merge_verify: dotnet build passed", file=sys.stderr)
    return 0


def main() -> int:
    root = Path.cwd()
    dirty = files_with_markers(root, tracked_files(root))
    if dirty:
        print("merge_verify: leftover conflict markers:", file=sys.stderr)
        for rel in dirty:
            print(f"  {rel}", file=sys.stderr)
        return 1
    if needs_dotnet_build(merge_touched_paths(root)):
        return dotnet_build(root)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
