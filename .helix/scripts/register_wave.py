#!/usr/bin/env python3
"""Register ONE next-wave slice: validate it, upsert its MANIFEST row, refresh INDEX-next.md.

The next-wave process (contigo-next-process.yaml) writes the wave directly as
``reports/plan/slices/<wave>.yaml`` in the slice grammar the fan-out walks.
This script is the fail-closed gate between the decomposer and the checker:

  * grammar: one flow-mapping task per line (cut_nightly_slices.TASK_RE),
    integer phase ids, ``layer: backend|frontend`` (``web`` is normalised),
    ``status: live`` only, ``forks: []``;
  * DAG: acyclic, ``depends_on`` produced in a strictly earlier phase by exactly
    one task, ``produces`` unique, ids ``E##/F##/US##/T##``, prompts on disk;
  * caps: ``max_tasks`` live tasks / ``max_phases`` phases
    (``reports/plan/next-run.json``, CLI overrides, defaults 20 / 5);
  * single writer per file per phase (``scripts/check_single_writer.py``);
  * ``helix validate-wavespec`` when the Helix backend is reachable.

Usage (cwd = .helix):
  python scripts/register_wave.py --next-id            # print the next free wave id (w14, w15, ...)
  python scripts/register_wave.py --last-id            # print the last registered slice id (previous)
  python scripts/register_wave.py --wave w14           # validate + MANIFEST upsert + INDEX-next.md
  python scripts/register_wave.py --wave w14 --check   # validate only, write nothing
  python scripts/register_wave.py --wave w14 --max-tasks 12 --max-phases 4 --previous e13 --title "..."

Exit 0: registered / valid. Exit 1: at least one violation (all are printed).
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
from collections import defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE / "scripts"))

import cut_nightly_slices as base  # noqa: E402

SLICES = HERE / "reports" / "plan" / "slices"
MANIFEST = SLICES / "MANIFEST.yaml"
INDEX_NEXT = SLICES / "INDEX-next.md"
RUN_JSON = HERE / "reports" / "plan" / "next-run.json"
ENV_FILE = HERE / ".env"

CHAIN_ID_RE = re.compile(r"^[ew](\d{1,3})$")  # e01..e13, w14..; e1011 (4 digits) is a combined slice
TASK_ID_RE = re.compile(r"^E\d+/F\d+/US\d+/T\d+$")
TASK_LINE_HINT = re.compile(r"^\s*-\s*\{id:")
DEFAULT_CHECKS = ["github_auth", "github_org", "github_repos", "hitl_previous"]
DEFAULT_MAX_TASKS = 20
DEFAULT_MAX_PHASES = 5


# --------------------------------------------------------------------------- io


def load_manifest() -> dict:
    if not MANIFEST.is_file():
        raise SystemExit(f"missing {MANIFEST.relative_to(HERE).as_posix()}")
    data = json.loads(MANIFEST.read_text(encoding="utf-8"))
    if not isinstance(data.get("slices"), list):
        raise SystemExit("MANIFEST.yaml has no slices[]")
    return data


def load_run_params() -> dict:
    if RUN_JSON.is_file():
        try:
            return json.loads(RUN_JSON.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            return {}
    return {}


def _dotenv_value(name: str) -> str | None:
    if os.environ.get(name):
        return os.environ[name]
    if not ENV_FILE.is_file():
        return None
    for raw in ENV_FILE.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        if key.strip() == name:
            value = value.strip()
            if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
                value = value[1:-1]
            return value or None
    return None


def find_helix_cli() -> list[str] | None:
    """Return the argv prefix for the helix CLI, or None when unreachable."""
    candidates: list[Path] = []
    declared = _dotenv_value("HELIX_BACKEND")
    if declared:
        candidates.append(Path(declared))
    for ancestor in HERE.parents:
        candidates.append(ancestor / "helix" / "src" / "backend")
    for backend in candidates:
        exe = backend / ".venv" / "Scripts" / "helix.exe"
        if exe.is_file():
            return [str(exe)]
        posix = backend / ".venv" / "bin" / "helix"
        if posix.is_file():
            return [str(posix)]
    on_path = shutil.which("helix")
    if on_path:
        return [on_path]
    return None


# ------------------------------------------------------------------ chain ids


def next_id() -> str:
    data = load_manifest()
    highest = 13
    for row in data["slices"]:
        m = CHAIN_ID_RE.match(str(row.get("id", "")))
        if m:
            highest = max(highest, int(m.group(1)))
    return f"w{highest + 1}"


def last_id(exclude: str | None = None) -> str | None:
    data = load_manifest()
    last: str | None = None
    for row in data["slices"]:
        rid = str(row.get("id", ""))
        if not rid or rid == exclude or row.get("superseded_by"):
            continue
        last = rid
    return last


# ---------------------------------------------------------------- validation


def _comment_value(text: str, key: str) -> str | None:
    m = re.search(rf"^#\s*{re.escape(key)}:\s*(.+?)\s*$", text, re.M)
    return m.group(1).strip() if m else None


def validate_wave(
    wave: str,
    *,
    max_tasks: int,
    max_phases: int,
    normalise: bool,
) -> tuple[list[str], list[str], dict]:
    """Return (errors, warnings, summary). ``normalise`` rewrites pN / web in place."""
    errors: list[str] = []
    warnings: list[str] = []
    path = SLICES / f"{wave}.yaml"
    if not path.is_file():
        return [f"missing {path.relative_to(HERE).as_posix()}"], warnings, {}

    text = path.read_text(encoding="utf-8")
    normalised = base.normalize_master(text)
    if normalised != text:
        if normalise:
            path.write_text(normalised, encoding="utf-8")
            warnings.append("normalised phase ids / layer names in place")
            text = normalised
        else:
            errors.append("phase ids must be integers and layer must be backend|frontend (run without --check to normalise)")

    m = re.search(r"^waveId:\s*(\S+)\s*$", text, re.M)
    wave_id = m.group(1) if m else ""
    if not wave_id:
        errors.append("missing waveId")
    elif not wave_id.endswith(wave):
        errors.append(f"waveId {wave_id!r} must end with the slice id {wave!r} (use wave-next-{wave})")
    if not re.search(r"^status:\s*planned\s*$", text, re.M):
        errors.append("status must be `planned`")
    if not re.search(r"^forks:\s*\[\]\s*$", text, re.M):
        errors.append("missing `forks: []`")

    hinted = sum(1 for line in text.splitlines() if TASK_LINE_HINT.match(line))
    try:
        rows = base.parse_master(text)
    except SystemExit as exc:  # parse_master raises SystemExit on a broken layer/phase
        return [f"cannot parse wave file: {exc}"], warnings, {}
    if len(rows) != hinted:
        errors.append(
            f"{hinted - len(rows)} task line(s) are not in the canonical grammar "
            "`- {id: ..., prompt: ..., produces: [...], depends_on: [...], effort: S|M|L, layer: backend|frontend, status: live}`"
        )
    if not rows:
        errors.append("no tasks")
        return errors, warnings, {}

    phase_order: list[int] = []
    for ph, _ in rows:
        n = int(ph)
        if n not in phase_order:
            phase_order.append(n)
    if phase_order != list(range(1, len(phase_order) + 1)):
        errors.append(f"phases must be contiguous from 1, got {phase_order}")
    if len(phase_order) > max_phases:
        errors.append(f"{len(phase_order)} phases exceed max_phases={max_phases}")

    live = [(int(ph), t) for ph, t in rows]
    if len(live) > max_tasks:
        errors.append(f"{len(live)} live tasks exceed max_tasks={max_tasks} (move stories to status: queued)")

    produced_in: dict[str, list[tuple[str, int]]] = defaultdict(list)
    seen_ids: set[str] = set()
    for ph, t in live:
        tid = t["id"]
        if not TASK_ID_RE.match(tid):
            errors.append(f"{tid}: id is not E##/F##/US##/T##")
        if tid in seen_ids:
            errors.append(f"{tid}: duplicate task id")
        seen_ids.add(tid)
        if t["status"] != "live":
            errors.append(f"{tid}: status {t['status']!r} — only live tasks belong in the wave file")
        prompt = t["prompt"]
        if not prompt.startswith("reports/workitems/") or "/tasks/" not in prompt:
            errors.append(f"{tid}: prompt must point at reports/workitems/.../tasks/*.md (got {prompt})")
        elif not (HERE / prompt).is_file():
            errors.append(f"{tid}: prompt file missing on disk: {prompt}")
        if t["effort"] not in base.TOKENS_BY_EFFORT:
            errors.append(f"{tid}: effort must be S|M|L")
        if not t["produces"]:
            errors.append(f"{tid}: produces is empty")
        for art in t["produces"]:
            if not re.match(r"^[a-z0-9][a-z0-9-]*$", art):
                errors.append(f"{tid}: produces {art!r} is not a kebab-case artifact name")
            produced_in[art].append((tid, ph))
    for art, owners in produced_in.items():
        if len(owners) > 1:
            errors.append(f"artifact {art!r} produced by {', '.join(o for o, _ in owners)} — must be exactly one task")
    for ph, t in live:
        for dep in t["depends_on"]:
            owners = produced_in.get(dep)
            if not owners:
                errors.append(f"{t['id']}: depends_on {dep!r} is produced by no task of this wave (drop it or add the producer)")
                continue
            _, dep_phase = owners[0]
            if dep_phase >= ph:
                errors.append(f"{t['id']} (phase {ph}): depends_on {dep!r} is produced in phase {dep_phase}, not strictly earlier")

    # single writer per file per phase
    proc = subprocess.run(
        [sys.executable, str(HERE / "scripts" / "check_single_writer.py"), "--slice", wave],
        cwd=HERE,
        capture_output=True,
        text=True,
        check=False,
    )
    if proc.returncode != 0:
        detail = (proc.stdout + proc.stderr).strip()
        errors.append("check_single_writer.py --slice " + wave + " failed:\n" + detail)

    # helix validate-wavespec (best effort: the backend may not be reachable in CI)
    helix = find_helix_cli()
    if helix and not errors:
        proc = subprocess.run(
            [*helix, "validate-wavespec", str(path)],
            cwd=HERE,
            capture_output=True,
            text=True,
            check=False,
        )
        if proc.returncode != 0:
            errors.append("helix validate-wavespec failed:\n" + (proc.stdout + proc.stderr).strip())
    elif not helix:
        warnings.append("helix CLI not found (HELIX_BACKEND / ../helix/src/backend); validate-wavespec skipped")

    stories = sorted({base.story_id_of(t["id"]) for _, t in live}, key=base.story_sort_key)
    epics = sorted({base.epic_id_of(t["id"]) for _, t in live})
    summary = {
        "wave_id": wave_id,
        "title": _comment_value(text, "title"),
        "source": _comment_value(text, "source"),
        "requirements": _comment_value(text, "requirements"),
        "phases": len(phase_order),
        "tasks": len(live),
        "tokens": sum(base.task_tokens(t) for _, t in live),
        "stories": stories,
        "epics": epics,
    }
    return errors, warnings, summary


# -------------------------------------------------------------- registration


def upsert_manifest(wave: str, summary: dict, *, previous: str | None, title: str | None) -> dict:
    data = load_manifest()
    run = load_run_params()
    row = {
        "id": wave,
        "title": title or summary.get("title") or f"Wave {wave} (next-wave process)",
        "previous": previous,
        "tokens": summary["tokens"],
        "tasks": summary["tasks"],
        "epic": ", ".join(summary["epics"]),
        "integration": False,
        "checks": list(DEFAULT_CHECKS),
        "stories": summary["stories"],
        "mode": "next",
        "source": summary.get("source") or run.get("todo"),
        "requirements": summary.get("requirements") or f"reports/context/waves/{wave}-requirements.md",
    }
    replaced = False
    for i, existing in enumerate(data["slices"]):
        if existing.get("id") == wave:
            data["slices"][i] = row
            replaced = True
            break
    if not replaced:
        data["slices"].append(row)
    MANIFEST.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    return row


def write_index_next() -> None:
    data = load_manifest()
    rows = [r for r in data["slices"] if r.get("mode") == "next"]
    lines = [
        "# Next-wave slices",
        "",
        "Produced by `python scripts/register_wave.py --wave <id>` from the wave the",
        "next-wave process (`contigo-next-process.yaml`) cut. One wave per run; the",
        "backlog keeps everything, the wave carries at most the cap.",
        "",
        "Launch (after reviewing `reports/audit/<id>-hitl.md`):",
        "",
        "```",
        "python scripts/check_slice_prereqs.py --slice <id>",
        "./run.ps1 -Max -Slice <id> -o execution-fanout      # or ./run-next.ps1 -LaunchOnly -Wave <id>",
        "```",
        "",
        "| Slice | Tasks | Tokens | Previous | Epics | Title | Source |",
        "|-------|-------|--------|----------|-------|-------|--------|",
    ]
    for r in rows:
        lines.append(
            f"| `{r['id']}` | {r.get('tasks', '?')} | {base.fmt_millions(int(r.get('tokens', 0)))} | "
            f"{r.get('previous') or '—'} | {r.get('epic', '')} | {r.get('title', '')} | {r.get('source') or ''} |"
        )
    INDEX_NEXT.write_text("\n".join(lines) + "\n", encoding="utf-8")


# --------------------------------------------------------------------- main


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="Validate and register one next-wave slice")
    ap.add_argument("--wave", help="slice id, e.g. w14")
    ap.add_argument("--next-id", action="store_true", help="print the next free wave id and exit")
    ap.add_argument("--last-id", action="store_true", help="print the last registered slice id and exit")
    ap.add_argument("--check", action="store_true", help="validate only; write nothing")
    ap.add_argument("--max-tasks", type=int, default=None)
    ap.add_argument("--max-phases", type=int, default=None)
    ap.add_argument("--previous", default=None, help="previous slice id for the HITL chain")
    ap.add_argument("--title", default=None)
    args = ap.parse_args(argv)

    if args.next_id:
        print(next_id())
        return 0
    if args.last_id:
        print(last_id() or "")
        return 0
    if not args.wave:
        ap.error("--wave <id> is required (or --next-id / --last-id)")
    wave = args.wave.lower()
    if not CHAIN_ID_RE.match(wave):
        print(f"ERROR: wave id {wave!r} must look like w14", file=sys.stderr)
        return 1

    run = load_run_params()
    max_tasks = args.max_tasks or int(run.get("max_tasks") or DEFAULT_MAX_TASKS)
    max_phases = args.max_phases or int(run.get("max_phases") or DEFAULT_MAX_PHASES)

    errors, warnings, summary = validate_wave(
        wave, max_tasks=max_tasks, max_phases=max_phases, normalise=not args.check
    )
    for w in warnings:
        print(f"WARNING: {w}", file=sys.stderr)
    if errors:
        print(f"register_wave: {wave} is NOT valid ({len(errors)} problem(s)):", file=sys.stderr)
        for e in errors:
            print(f"  - {e}", file=sys.stderr)
        return 1

    print(
        f"{wave}: {summary['tasks']} live tasks in {summary['phases']} phases, "
        f"{len(summary['stories'])} stories, epics {', '.join(summary['epics'])}, "
        f"~{base.fmt_millions(summary['tokens'])} tokens (caps {max_tasks}/{max_phases})"
    )
    if args.check:
        print("check only — MANIFEST not written")
        return 0

    previous = args.previous or run.get("previous") or last_id(exclude=wave)
    row = upsert_manifest(wave, summary, previous=previous, title=args.title)
    write_index_next()
    print(
        f"registered {wave} in MANIFEST.yaml (previous: {row['previous'] or 'none'}); "
        f"INDEX-next.md refreshed"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
