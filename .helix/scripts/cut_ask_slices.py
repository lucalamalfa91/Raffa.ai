#!/usr/bin/env python3
"""Cut wave-spec.ask.yaml into slices/e13.yaml only (Ask Contigo V2).

Never writes slice.current.yaml.
Never overwrites slices/e01.yaml–e11.yaml, e1011.yaml, e12.yaml (superseded),
or other wave-specs.
Upserts e13 in the live MANIFEST.yaml (previous: e1011) and marks the e12
row `superseded_by: e13` (HITL 2026-09-08, inputs/requirements.md §0 D4).

Usage (cwd = .helix):
  python scripts/cut_ask_slices.py
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE / "scripts"))

import cut_nightly_slices as base  # noqa: E402

MASTER = HERE / "reports" / "plan" / "wave-spec.ask.yaml"
OUT_DIR = HERE / "reports" / "plan" / "slices"
INDEX_ASK = OUT_DIR / "INDEX-ask.md"
MANIFEST_ASK = OUT_DIR / "MANIFEST-ask.yaml"
LIVE_MANIFEST = OUT_DIR / "MANIFEST.yaml"
EPIC_NUMBER = 13
SLICE_ID = f"e{EPIC_NUMBER}"
PREVIOUS = "e1011"
SUPERSEDED_SLICE = "e12"
PROTECTED_SLICES = frozenset({*(f"e{n:02d}" for n in range(1, 12)), "e1011", SUPERSEDED_SLICE})
ASK_TITLE = "E13 Ask Contigo V2 (documents, conversations, market feed, strategies, system-aware)"


def manifest_row(*, tokens: int, tasks: int, stories: list[str]) -> dict:
    return {
        "id": SLICE_ID,
        "title": ASK_TITLE,
        "previous": PREVIOUS,
        "epic": f"E{EPIC_NUMBER}",
        "integration": False,
        "checks": ["github_auth", "github_org", "github_repos", "hitl_previous"],
        "stories": stories,
        "tokens": tokens,
        "tasks": tasks,
        "design_oracle": "inputs/design/prototypes/Contigo V2 Prototype.html",
        "requirements": "inputs/requirements.md",
    }


def upsert_live_manifest(*, tokens: int, tasks: int, stories: list[str]) -> None:
    if not LIVE_MANIFEST.is_file():
        print(f"missing {LIVE_MANIFEST}", file=sys.stderr)
        raise SystemExit(1)
    data = json.loads(LIVE_MANIFEST.read_text(encoding="utf-8"))
    slices = data.get("slices")
    if not isinstance(slices, list):
        print("MANIFEST.yaml has no slices[]", file=sys.stderr)
        raise SystemExit(1)
    row = manifest_row(tokens=tokens, tasks=tasks, stories=stories)
    replaced = False
    for i, existing in enumerate(slices):
        if existing.get("id") == SLICE_ID:
            slices[i] = row
            replaced = True
        elif existing.get("id") == SUPERSEDED_SLICE and existing.get("superseded_by") != SLICE_ID:
            existing["superseded_by"] = SLICE_ID
            if "(superseded" not in existing.get("title", ""):
                existing["title"] = f"{existing.get('title', SUPERSEDED_SLICE)} (superseded by {SLICE_ID}; never launched)"
    if not replaced:
        slices.append(row)
    LIVE_MANIFEST.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")


def main() -> int:
    if not MASTER.exists():
        print(f"missing {MASTER}", file=sys.stderr)
        return 1
    text = MASTER.read_text(encoding="utf-8")
    if "waveId: placeholder" in text or "phases: []" in text:
        print("wave-spec.ask.yaml is still a placeholder; skip slice cut", file=sys.stderr)
        return 1
    rows = base.parse_master(text)
    live = [(ph, t) for ph, t in rows if t["status"] == "live"]
    if not live:
        print("no live tasks in wave-spec.ask.yaml", file=sys.stderr)
        return 1
    for _, t in live:
        epic = base.epic_id_of(t["id"])
        n = int(epic[1:])
        if n != EPIC_NUMBER:
            print(
                f"refusing to slice {t['id']}: ask cutter is E{EPIC_NUMBER} only",
                file=sys.stderr,
            )
            return 1
    packed, _warnings = base.pack_all(live, omit_features=frozenset())
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    written: list[str] = []
    for sl in packed:
        if sl.slice_id in PROTECTED_SLICES:
            print(f"refusing to overwrite protected slice {sl.slice_id}", file=sys.stderr)
            return 1
        if sl.slice_id != SLICE_ID:
            print(f"unexpected slice id {sl.slice_id}; expected {SLICE_ID}", file=sys.stderr)
            return 1
        selected = base.drop_external_deps(sl.selected)
        body = base.emit_yaml(sl.slice_id, ASK_TITLE, selected, sl.tokens)
        body = body.replace(
            "phases:\n",
            "# Design oracle: inputs/design/prototypes/Contigo V2 Prototype.html "
            "(unpacked: inputs/design/prototypes/contigo-v2/). Requirements: inputs/requirements.md.\n"
            "# Studio: contigo-process.yaml -> execution-fanout after copying this file to slice.current.yaml.\n"
            "phases:\n",
            1,
        )
        path = OUT_DIR / f"{sl.slice_id}.yaml"
        path.write_text(body, encoding="utf-8")
        written.append(sl.slice_id)
        stories = [s.id for s in sl.stories]
        upsert_live_manifest(tokens=sl.tokens, tasks=len(selected), stories=stories)

    lines = [
        f"# Ask V2 slice ({SLICE_ID}). e01–e11, e1011 and e12 (superseded) stay put.",
        "",
        "mode: ask-v2",
        "master: reports/plan/wave-spec.ask.yaml",
        "design_oracle: inputs/design/prototypes/Contigo V2 Prototype.html",
        "requirements: inputs/requirements.md",
        "slices:",
    ]
    for sl in packed:
        lines.append(f"  - id: {sl.slice_id}")
        lines.append(f"    title: {ASK_TITLE}")
        lines.append(f"    tokens: {sl.tokens}")
        lines.append(f"    tasks: {len(sl.selected)}")
        lines.append(f"    previous: {PREVIOUS}")
        lines.append(f"    supersedes: {SUPERSEDED_SLICE}")
    MANIFEST_ASK.write_text("\n".join(lines) + "\n", encoding="utf-8")

    idx = [
        f"# Ask V2 slice ({SLICE_ID})",
        "",
        "Produced by `python scripts/cut_ask_slices.py` from `reports/plan/wave-spec.ask.yaml`.",
        "Supersedes e12 (never launched). Design oracle:",
        "`inputs/design/prototypes/Contigo V2 Prototype.html` (unpacked under",
        "`inputs/design/prototypes/contigo-v2/`). Requirements: `inputs/requirements.md`.",
        "",
        "Launch only after `reports/plan/gates/ask-v2.hitl-ok` exists and no other",
        "wave is running. From Helix Studio: open `contigo-process.yaml`, make sure",
        f"`reports/plan/slice.current.yaml` is this slice, run `execution-fanout`.",
        "Or from PowerShell:",
        "",
        "```",
        f"python scripts/check_slice_prereqs.py --slice {SLICE_ID}",
        f"./run.ps1 -Max -Slice {SLICE_ID} -o execution-fanout",
        "```",
        "",
        "## Files",
        "",
    ]
    for sid in written:
        idx.append(f"- `{sid}.yaml`")
    INDEX_ASK.write_text("\n".join(idx) + "\n", encoding="utf-8")
    print("wrote", ", ".join(written))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
