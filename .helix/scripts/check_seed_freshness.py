#!/usr/bin/env python3
"""Pre-run check: warn if ``integration`` is ahead of ``main``.

When ``resume_completed`` re-seeds ``integration`` from ``main``, any commits
that exist only on ``integration`` (e.g. from a prior partial wave or a manual
merge) are discarded.  This script exits 0 (observation-only) but prints a
loud warning so the operator can fast-forward ``main`` before launching.

Runs in the product clone root (cwd = .helix parent).
"""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path


def _git(*args: str, cwd: Path) -> str:
    return subprocess.check_output(
        ["git", *args], cwd=cwd, text=True, stderr=subprocess.DEVNULL
    ).strip()


def main() -> int:
    root = Path.cwd()
    # Walk up to the product clone root if cwd is .helix
    if (root / "raffa-process.yaml").exists() and (root.parent / ".git").exists():
        root = root.parent

    try:
        _git("rev-parse", "--verify", "--quiet", "refs/heads/integration", cwd=root)
    except subprocess.CalledProcessError:
        # No integration branch — first run, nothing to check.
        return 0

    try:
        ahead = _git(
            "rev-list", "--count", "main..integration", cwd=root
        )
    except subprocess.CalledProcessError:
        return 0

    count = int(ahead)
    if count > 0:
        print(
            f"WARNING: integration is {count} commit(s) ahead of main. "
            f"resume_completed will re-seed integration from main, "
            f"discarding those commits. Consider: git checkout main && "
            f"git merge --ff-only integration",
            file=sys.stderr,
        )
        # Also check if main is a strict ancestor — if so, ff is safe.
        try:
            _git("merge-base", "--is-ancestor", "main", "integration", cwd=root)
            print(
                "  (main IS an ancestor of integration — fast-forward is safe)",
                file=sys.stderr,
            )
        except subprocess.CalledProcessError:
            print(
                "  (main is NOT an ancestor — manual merge required)",
                file=sys.stderr,
            )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
