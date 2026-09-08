#!/usr/bin/env python3
"""Snapshot / verify that the live plan was not rewritten by ask-copilot.

Hash-locks e01–e11, e1011, prior wave-specs, locked ADRs (not 001/004/011/018/020/023),
epic-01…11. Does NOT lock slice.current.yaml.

Usage (cwd = .helix):
  python scripts/assert_ask_plan_untouched.py snapshot
  python scripts/assert_ask_plan_untouched.py verify
"""

from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
SNAP = HERE / "reports" / "plan" / ".ask-protect-snapshot.json"

SLICE_IDS = tuple([*(f"e{n:02d}" for n in range(1, 12)), "e1011"])
WAVE_SPECS = (
    "wave-spec.execution.yaml",
    "wave-spec.web.yaml",
    "wave-spec.schema.yaml",
    "wave-spec.readiness.yaml",
    "wave-spec.visual.yaml",
)
# Writable by this process: 001, 004, 011, 018, 020, 023.
LOCKED_ADR_NUMBERS = (
    *range(2, 4),
    *range(5, 11),
    *range(12, 18),
    19,
    21,
    22,
)
EPIC_GLOBS = tuple(
    [*(f"epic-0{n}-*" for n in range(1, 10)), "epic-10-*", "epic-11-*"]
)


def _sha256(path: Path) -> str:
    h = hashlib.sha256()
    h.update(path.read_bytes())
    return h.hexdigest()


def _rel(path: Path) -> str:
    return path.relative_to(HERE).as_posix()


def _locked_files() -> list[Path]:
    plan = HERE / "reports" / "plan"
    out: list[Path] = [plan / name for name in WAVE_SPECS]
    slices = plan / "slices"
    for sid in SLICE_IDS:
        out.append(slices / f"{sid}.yaml")
    arch = HERE / "reports" / "architecture"
    for n in LOCKED_ADR_NUMBERS:
        matches = sorted(arch.glob(f"ADR-{n:03d}*.md"))
        out.extend(p for p in matches if p.parent == arch)
    work = HERE / "reports" / "workitems"
    for pat in EPIC_GLOBS:
        for epic_dir in sorted(work.glob(pat)):
            if epic_dir.is_dir():
                out.extend(sorted(p for p in epic_dir.rglob("*") if p.is_file()))
    return out


def snapshot() -> int:
    files = _locked_files()
    missing = [str(_rel(p)) for p in files if not p.is_file()]
    if missing:
        print("snapshot: missing protected files:", file=sys.stderr)
        for m in missing:
            print(f"  {m}", file=sys.stderr)
        return 1
    payload = {
        "files": {_rel(p): _sha256(p) for p in files},
        "index_must_contain": [f"ADR-{n:03d}" for n in range(1, 23)],
        "backlog_must_contain": [
            *(f"epic-0{n}" for n in range(1, 10)),
            "epic-10",
            "epic-11",
        ],
    }
    SNAP.parent.mkdir(parents=True, exist_ok=True)
    SNAP.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(f"snapshot: {len(payload['files'])} protected files")
    return 0


def verify() -> int:
    if not SNAP.is_file():
        print("verify: missing snapshot — run snapshot first", file=sys.stderr)
        return 1
    payload = json.loads(SNAP.read_text(encoding="utf-8"))
    broken: list[str] = []
    for rel, expected in payload["files"].items():
        path = HERE / rel
        if not path.is_file():
            broken.append(f"DELETED {rel}")
            continue
        if _sha256(path) != expected:
            broken.append(f"CHANGED {rel}")
    index = HERE / "reports" / "architecture" / "INDEX.md"
    if index.is_file():
        text = index.read_text(encoding="utf-8")
        for needle in payload.get("index_must_contain", []):
            if needle not in text:
                broken.append(f"INDEX.md lost {needle}")
    else:
        broken.append("DELETED reports/architecture/INDEX.md")
    backlog = HERE / "reports" / "workitems" / "BACKLOG.md"
    if backlog.is_file():
        text = backlog.read_text(encoding="utf-8")
        for needle in payload.get("backlog_must_contain", []):
            if needle not in text:
                broken.append(f"BACKLOG.md lost {needle}")
    if broken:
        print("verify: live plan was mutated — abort", file=sys.stderr)
        for b in broken:
            print(f"  {b}", file=sys.stderr)
        return 1
    print(
        "verify: protected plan unchanged "
        "(e01-e11 e1011, locked ADRs, epic-01..11)"
    )
    return 0


def main() -> int:
    if len(sys.argv) != 2 or sys.argv[1] not in {"snapshot", "verify"}:
        print("usage: assert_ask_plan_untouched.py snapshot|verify", file=sys.stderr)
        return 2
    return snapshot() if sys.argv[1] == "snapshot" else verify()


if __name__ == "__main__":
    raise SystemExit(main())
