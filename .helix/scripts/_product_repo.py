"""Resolve the Raffa *product* clone, not the nested `.helix` git.

`ensure_artifact_git` used to `git init` inside `.helix` so Helix would not
worktree `helix-artifacts`. After `.helix` landed inside `lucalamalfa91/raffa`,
that nest became the hook cwd: no `origin`, a private `integration`, and
`on_orchestration_stop` exited 1 (fail-open). Studio stayed green; no PR.

Walk up from this artifact until a repo whose `origin` is the product remote,
or that has `infra/` + `backend/` at its root. Fall back to the parent of
`.helix` when that parent is itself a git toplevel.
"""

from __future__ import annotations

import subprocess
from pathlib import Path

ARTIFACT = Path(__file__).resolve().parents[1]
PRODUCT_MARKERS = ("infra", "backend", "web", "mobile")
ORIGIN_NEEDLES = ("github.com/lucalamalfa91/raffa", "lucalamalfa91/raffa.git")


def _git(cwd: Path, *args: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["git", *args],
        cwd=cwd,
        check=False,
        text=True,
        capture_output=True,
    )


def _toplevel(start: Path) -> Path | None:
    proc = _git(start, "rev-parse", "--show-toplevel")
    if proc.returncode != 0:
        return None
    raw = (proc.stdout or "").strip()
    return Path(raw) if raw else None


def _origin(repo: Path) -> str:
    return (_git(repo, "remote", "get-url", "origin").stdout or "").strip()


def _looks_like_product(repo: Path) -> bool:
    origin = _origin(repo).lower().replace("\\", "/")
    if any(needle in origin for needle in ORIGIN_NEEDLES):
        return True
    return all((repo / name).exists() for name in PRODUCT_MARKERS)


def product_repo() -> Path:
    here = ARTIFACT.resolve()
    cursor = here
    seen: set[Path] = set()
    while cursor not in seen:
        seen.add(cursor)
        top = _toplevel(cursor)
        if top is not None:
            top = top.resolve()
            if _looks_like_product(top):
                return top
            if top != here and top not in seen:
                cursor = top.parent
                continue
        parent = cursor.parent
        if parent == cursor:
            break
        cursor = parent

    parent = here.parent
    parent_top = _toplevel(parent)
    if parent_top is not None:
        return parent_top.resolve()
    return here
