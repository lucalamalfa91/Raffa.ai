#!/usr/bin/env python3
"""Snapshot / verify that the next-wave process only APPENDED to the live plan.

The next-wave process (contigo-next-process.yaml) may create files for its own
wave, append to the shared indexes, and add footers to ADRs. It must never
rewrite what an earlier council or wave produced, and it must never edit the
raw inputs. This script is generic: it does not know the wave's epics ahead of
time, so it locks what EXISTS at snapshot time and, after the run, checks that
every locked file is either byte-identical or a pure extension of itself.

Locked byte-identical:
  inputs/**                                  (raw inputs are the human's)
  reports/plan/wave-spec.*.yaml, reports/plan/slices/*.yaml (existing), slice.current.yaml
  reports/context/*.md (top level), reports/context/waves/* of OTHER waves
  reports/architecture/draft/** (existing), reports/architecture/waves/* of OTHER waves
  reports/audit/*, reports/execution/*  (existing, other waves)
  contigo-process.yaml, run.ps1, run.sh

Locked append-only (new text may follow the old text):
  reports/architecture/ADR-*.md   (status line may change: superseded)
  reports/architecture/INDEX.md, reports/workitems/BACKLOG.md, reports/open-questions.md
  reports/workitems/** (existing; frontmatter `status:` line may change: superseded)
  reports/plan/slices/MANIFEST.yaml: existing rows (other ids) must be unchanged

Files whose name contains the current wave id (from reports/plan/next-run.json,
or --wave) are excluded, so a re-run of the same wave may regenerate them.

Usage (cwd = .helix):
  python scripts/assert_next_plan_untouched.py snapshot [--wave w14]
  python scripts/assert_next_plan_untouched.py verify   [--wave w14]

Exit 0: clean. Exit 1: a locked file was rewritten or deleted. Exit 2: no snapshot.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
SNAP = HERE / "reports" / "plan" / ".next-protect-snapshot.json"
RUN_JSON = HERE / "reports" / "plan" / "next-run.json"
MANIFEST = HERE / "reports" / "plan" / "slices" / "MANIFEST.yaml"

_FRONTMATTER_STATUS = re.compile(r"^status:\s.*$", re.M)
_ADR_STATUS = re.compile(r"^- \*\*Status\*\*:.*$", re.M)


def _rel(path: Path) -> str:
    return path.relative_to(HERE).as_posix()


def _sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _text(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="replace").replace("\r\n", "\n")


def _normalised(path: Path, kind: str) -> str:
    text = _text(path)
    if kind == "workitem":
        m = re.match(r"^---\n(.*?)\n---", text, re.S)
        if m:
            fm = _FRONTMATTER_STATUS.sub("", m.group(1))
            text = "---\n" + fm + "\n---" + text[m.end() :]
    elif kind == "adr":
        text = _ADR_STATUS.sub("", text)
    return text


def _wave_id(explicit: str | None) -> str | None:
    if explicit:
        return explicit.lower()
    if RUN_JSON.is_file():
        try:
            return str(json.loads(RUN_JSON.read_text(encoding="utf-8")).get("wave") or "").lower() or None
        except json.JSONDecodeError:
            return None
    return None


def _is_current_wave_file(path: Path, wave: str | None) -> bool:
    if not wave:
        return False
    rel = _rel(path).lower()
    name = path.name.lower()
    if wave in name.split(".")[0].split("-"):
        return True
    # epics created by this wave: frontmatter `wave: <wave>` on the epic file
    if rel.startswith("reports/workitems/epic-"):
        epic_dir = HERE / Path(rel).parts[0] / Path(rel).parts[1] / Path(rel).parts[2]
        epic_md = epic_dir / f"{epic_dir.name}.md"
        if epic_md.is_file():
            head = _text(epic_md)[:600]
            if re.search(rf"^wave:\s*{re.escape(wave)}\s*$", head, re.M | re.I):
                return True
    return False


def _files(pattern_root: Path, glob: str) -> list[Path]:
    return sorted(p for p in pattern_root.glob(glob) if p.is_file())


def collect(wave: str | None) -> tuple[dict[str, str], dict[str, dict]]:
    identical: dict[str, str] = {}
    append_only: dict[str, dict] = {}

    def lock_identical(paths: list[Path]) -> None:
        for p in paths:
            if _is_current_wave_file(p, wave):
                continue
            identical[_rel(p)] = _sha(p.read_bytes())

    def lock_append(paths: list[Path], kind: str) -> None:
        for p in paths:
            if _is_current_wave_file(p, wave):
                continue
            norm = _normalised(p, kind)
            append_only[_rel(p)] = {"kind": kind, "len": len(norm), "sha": _sha(norm.encode("utf-8"))}

    inputs = HERE / "inputs"
    lock_identical([p for p in inputs.rglob("*") if p.is_file() and "node_modules" not in p.parts])
    plan = HERE / "reports" / "plan"
    lock_identical(_files(plan, "wave-spec.*.yaml") + _files(plan, "slice.current.yaml") + _files(plan / "slices", "*.yaml"))
    identical.pop(_rel(MANIFEST), None)  # rows are checked individually
    context = HERE / "reports" / "context"
    lock_identical(_files(context, "*.md") + _files(context / "waves", "*.md"))
    arch = HERE / "reports" / "architecture"
    lock_identical([p for p in (arch / "draft").rglob("*") if p.is_file()] + _files(arch / "waves", "*.md"))
    lock_identical(_files(HERE / "reports" / "audit", "*.md") + _files(HERE / "reports" / "execution", "*.md"))
    lock_identical([HERE / n for n in ("contigo-process.yaml", "run.ps1", "run.sh") if (HERE / n).is_file()])

    lock_append(_files(arch, "ADR-*.md"), "adr")
    lock_append([p for p in (arch / "INDEX.md", HERE / "reports" / "workitems" / "BACKLOG.md", HERE / "reports" / "open-questions.md") if p.is_file()], "text")
    work = HERE / "reports" / "workitems"
    lock_append([p for p in work.rglob("*.md") if p.is_file() and p.name != "BACKLOG.md"], "workitem")
    return identical, append_only


def _manifest_rows(exclude: str | None) -> dict[str, str]:
    if not MANIFEST.is_file():
        return {}
    data = json.loads(MANIFEST.read_text(encoding="utf-8"))
    rows: dict[str, str] = {}
    for row in data.get("slices", []):
        rid = str(row.get("id", ""))
        if rid and rid != exclude:
            rows[rid] = json.dumps(row, sort_keys=True)
    return rows


def snapshot(wave: str | None) -> int:
    identical, append_only = collect(wave)
    payload = {
        "wave": wave,
        "identical": identical,
        "append_only": append_only,
        "manifest_rows": _manifest_rows(wave),
    }
    SNAP.parent.mkdir(parents=True, exist_ok=True)
    SNAP.write_text(json.dumps(payload, indent=1) + "\n", encoding="utf-8")
    print(
        f"snapshot: wave={wave or '?'} {len(identical)} byte-locked, "
        f"{len(append_only)} append-only, {len(payload['manifest_rows'])} manifest rows"
    )
    return 0


def verify(wave: str | None) -> int:
    if not SNAP.is_file():
        print("verify: no snapshot (reports/plan/.next-protect-snapshot.json) — run `snapshot` first "
              "(run-next.ps1 does it); not verifiable", file=sys.stderr)
        return 2
    payload = json.loads(SNAP.read_text(encoding="utf-8"))
    wave = wave or payload.get("wave")
    broken: list[str] = []
    for rel, expected in payload["identical"].items():
        p = HERE / rel
        if not p.is_file():
            broken.append(f"DELETED  {rel}")
        elif _sha(p.read_bytes()) != expected:
            broken.append(f"REWRITTEN {rel} (byte-locked)")
    for rel, meta in payload["append_only"].items():
        p = HERE / rel
        if not p.is_file():
            broken.append(f"DELETED  {rel}")
            continue
        norm = _normalised(p, meta["kind"])
        prefix = norm[: meta["len"]]
        if _sha(prefix.encode("utf-8")) != meta["sha"]:
            broken.append(f"REWRITTEN {rel} (append-only: the original text must stay as a prefix)")
    rows_now = _manifest_rows(wave)
    for rid, blob in payload.get("manifest_rows", {}).items():
        if rid not in rows_now:
            broken.append(f"MANIFEST row {rid} removed")
        elif rows_now[rid] != blob:
            broken.append(f"MANIFEST row {rid} changed")
    if broken:
        print("verify: the live plan was mutated — abort", file=sys.stderr)
        for b in broken:
            print(f"  {b}", file=sys.stderr)
        return 1
    print(
        f"verify: protected plan unchanged (wave={wave or '?'}; "
        f"{len(payload['identical'])} byte-locked, {len(payload['append_only'])} append-only)"
    )
    return 0


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("mode", choices=["snapshot", "verify"])
    ap.add_argument("--wave", default=None)
    args = ap.parse_args()
    wave = _wave_id(args.wave)
    return snapshot(wave) if args.mode == "snapshot" else verify(wave)


if __name__ == "__main__":
    raise SystemExit(main())
