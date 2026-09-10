#!/usr/bin/env python3
"""Keep fan-out pointed at the product clone when `.helix` lives inside it.

Historically this script `git init`'d `.helix` so Helix would not worktree
`helix-artifacts`. After `.helix` was committed into `lucalamalfa91/raffa`,
that nest hid `origin` from `open_fanout_pr.py` (r0-a: hook exit 1, Studio green).

Now: if the parent directory is already the product clone, do **not** nest.
Only init `.helix` when the walk-up would land on a different repo
(e.g. helix-artifacts).
"""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
_HELIX_GIT_EMAIL = "helix@localhost"
_HELIX_GIT_NAME = "Helix Runner"


def _git(*args: str, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["git", *args],
        cwd=HERE,
        check=check,
        text=True,
        capture_output=True,
    )


def _toplevel() -> Path | None:
    proc = _git("rev-parse", "--show-toplevel", check=False)
    if proc.returncode != 0:
        return None
    raw = (proc.stdout or "").strip()
    return Path(raw) if raw else None


def _parent_is_product(parent: Path) -> bool:
    proc = subprocess.run(
        ["git", "remote", "get-url", "origin"],
        cwd=parent,
        check=False,
        text=True,
        capture_output=True,
    )
    origin = (proc.stdout or "").strip().lower().replace("\\", "/")
    if "lucalamalfa91/raffa" in origin:
        return True
    return all((parent / name).exists() for name in ("infra", "backend", "web", "mobile"))


def main() -> int:
    top = _toplevel()
    here = HERE.resolve()
    parent = here.parent

    if _parent_is_product(parent):
        print(
            "[ensure_artifact_git] parent is the product clone "
            f"({parent}); not nesting .helix (PR hook needs that origin)"
        )
        if top is not None and top.resolve() == here:
            print(
                "[ensure_artifact_git] leftover nested .helix/.git is still "
                "the Helix walk-up target. Rename it to .git.nest.bak when no "
                "wave is running so fan-out uses the product clone.",
                file=sys.stderr,
            )
        return 0

    if top is not None and top.resolve() == here:
        branches = _git("branch", "--list", "main").stdout.strip()
        if not branches:
            print(
                "ERROR: .helix is a git repo but has no 'main' branch "
                "(execution-fanout base_branch: main).",
                file=sys.stderr,
            )
            return 1
        print("[ensure_artifact_git] .helix is its own git toplevel on main")
        return 0

    if top is not None:
        print(f"[ensure_artifact_git] nested init: parent toplevel is {top}")

    _git("init", "-b", "main")
    _git("config", "user.email", _HELIX_GIT_EMAIL)
    _git("config", "user.name", _HELIX_GIT_NAME)
    _git("add", "-A")
    status = _git("status", "--porcelain").stdout.strip()
    if status:
        _git("commit", "-m", "helix: initialize artifact repo for fan-out worktrees")
    else:
        _git(
            "commit",
            "--allow-empty",
            "-m",
            "helix: initialize artifact repo for fan-out worktrees",
        )
    print("[ensure_artifact_git] initialized .helix git repo on main")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
