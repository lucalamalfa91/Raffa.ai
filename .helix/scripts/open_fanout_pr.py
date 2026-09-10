#!/usr/bin/env python3
"""Helix `on_orchestration_stop` hook: push product `integration` and open a PR.

Must operate on the *product* clone (`lucalamalfa91/raffa`), never the
nested `.helix` git that `ensure_artifact_git` used to create. That nest has
no `origin`; the r0-a hook exited 1 there and Helix stayed green (observation,
fail-open).

Helix runs this as an external command hook:
  - envelope JSON on stdin (ignored)
  - empty stdout = continue
  - log on stderr
  - child env is PATH/HOME/LANG/TMPDIR only
  - non-zero exit is recorded; the wave still completed
"""

from __future__ import annotations

import shutil
import subprocess
import sys

from _product_repo import _git, _origin, product_repo

CURRENT_NAME = "reports/plan/slice.current.yaml"


def _log(msg: str) -> None:
    print(msg, file=sys.stderr)


def _slice_id(repo) -> str:
    current = repo / CURRENT_NAME
    # slice.current lives in the artifact; also try artifact-relative via repo/.helix
    for path in (current, repo / ".helix" / CURRENT_NAME):
        if path.is_file():
            for line in path.read_text(encoding="utf-8").splitlines():
                if line.startswith("waveId:"):
                    return line.split(":", 1)[1].strip().strip("'\"") or "slice"
    return "slice"


def main() -> int:
    if not sys.stdin.isatty():
        sys.stdin.read()

    repo = product_repo()
    _log(f"[open_fanout_pr] product_repo={repo}")

    top = _git(repo, "rev-parse", "--show-toplevel")
    if top.returncode != 0:
        _log("ERROR: product path is not a git repo.")
        return 1

    if not _origin(repo):
        _log(
            "ERROR: product repo has no `origin`. "
            "The nested `.helix` git must not be used for this hook."
        )
        return 1

    has_integration = _git(repo, "rev-parse", "-q", "--verify", "refs/heads/integration")
    if has_integration.returncode != 0:
        _log("[open_fanout_pr] no integration branch on the product clone; nothing to open")
        return 0

    vs_main = _git(repo, "rev-list", "--count", "main..integration")
    if vs_main.returncode != 0 or vs_main.stdout.strip() == "0":
        _log("[open_fanout_pr] integration has no commits ahead of local main; skip PR")
        return 0

    if shutil.which("gh") is None:
        _log("ERROR: `gh` is not on PATH. Install GitHub CLI and run `gh auth login`.")
        return 1

    push = _git(repo, "push", "-u", "origin", "integration")
    if push.returncode != 0:
        _log(push.stderr or push.stdout)
        _log("ERROR: could not push integration to origin.")
        return 1
    _log("[open_fanout_pr] pushed origin/integration")

    existing = subprocess.run(
        [
            "gh",
            "pr",
            "list",
            "--base",
            "main",
            "--head",
            "integration",
            "--state",
            "open",
            "--json",
            "url",
            "--jq",
            ".[0].url // empty",
        ],
        cwd=repo,
        check=False,
        text=True,
        capture_output=True,
    )
    url = (existing.stdout or "").strip()
    if url:
        _log(f"[open_fanout_pr] existing PR: {url}")
        return 0

    slice_id = _slice_id(repo)
    created = subprocess.run(
        [
            "gh",
            "pr",
            "create",
            "--base",
            "main",
            "--head",
            "integration",
            "--title",
            f"slice {slice_id}: integration → main",
            "--body",
            "Helix `execution-fanout` completed this slice. "
            "Phase-barrier merges are on `integration`. Review here; "
            "do not land onto local `main` until this PR merges. "
            "Read `.helix/reports/execution/wave-close.md` for warnings.",
        ],
        cwd=repo,
        check=False,
        text=True,
        capture_output=True,
    )
    if created.returncode != 0:
        _log(created.stderr or created.stdout)
        _log("ERROR: gh pr create failed.")
        return 1

    _log(f"[open_fanout_pr] opened PR: {(created.stdout or '').strip()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
