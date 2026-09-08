#!/usr/bin/env python3
"""Cut wave-spec.ask.yaml into slices/e12.yaml only.

Never writes slice.current.yaml.
Never overwrites slices/e01.yaml–e11.yaml, e1011.yaml, or other wave-specs.
Upserts e12 in the live MANIFEST.yaml (previous: e1011).

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
PROTECTED_SLICES = frozenset({*(f"e{n:02d}" for n in range(1, 12)), "e1011"})
ASK_TITLE = "E12 Ask Contigo savings copilot"

E12_MANIFEST_ROW = {
    "id": "e12",
    "title": "E12 Ask Contigo savings copilot",
    "previous": "e1011",
    "epic": "E12",
    "integration": False,
    "checks": [
        "github_auth",
        "github_org",
        "github_repos",
        "hitl_previous",
    ],
    "stories": [
        "E12/F01/US01",
        "E12/F02/US01",
        "E12/F03/US01",
        "E12/F04/US01",
        "E12/F05/US01",
    ],
}


def upsert_live_manifest(*, tokens: int, tasks: int) -> None:
    if not LIVE_MANIFEST.is_file():
        print(f"missing {LIVE_MANIFEST}", file=sys.stderr)
        raise SystemExit(1)
    data = json.loads(LIVE_MANIFEST.read_text(encoding="utf-8"))
    slices = data.get("slices")
    if not isinstance(slices, list):
        print("MANIFEST.yaml has no slices[]", file=sys.stderr)
        raise SystemExit(1)
    row = dict(E12_MANIFEST_ROW)
    row["tokens"] = tokens
    row["tasks"] = tasks
    replaced = False
    for i, existing in enumerate(slices):
        if existing.get("id") == "e12":
            slices[i] = row
            replaced = True
            break
    if not replaced:
        slices.append(row)
    LIVE_MANIFEST.write_text(
        json.dumps(data, indent=2) + "\n", encoding="utf-8"
    )


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
        if n != 12:
            print(
                f"refusing to slice {t['id']}: ask cutter is E12 only",
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
        selected = base.drop_external_deps(sl.selected)
        body = base.emit_yaml(sl.slice_id, ASK_TITLE, selected, sl.tokens)
        path = OUT_DIR / f"{sl.slice_id}.yaml"
        path.write_text(body, encoding="utf-8")
        written.append(sl.slice_id)
        upsert_live_manifest(tokens=sl.tokens, tasks=len(selected))

    lines = [
        "# Ask-copilot slice (e12). e01–e11 and e1011 stay put.",
        "",
        "mode: ask-copilot",
        "master: reports/plan/wave-spec.ask.yaml",
        "slices:",
    ]
    for sl in packed:
        lines.append(f"  - id: {sl.slice_id}")
        lines.append(f"    title: {ASK_TITLE}")
        lines.append(f"    tokens: {sl.tokens}")
        lines.append(f"    tasks: {len(sl.selected)}")
        lines.append("    previous: e1011")
    MANIFEST_ASK.write_text("\n".join(lines) + "\n", encoding="utf-8")

    idx = [
        "# Ask-copilot slice (e12)",
        "",
        "Produced by `python scripts/cut_ask_slices.py`.",
        "Launch only after ADR HITL **and** e1011 is closed",
        "(live process, not this YAML). Do **not** run in parallel with e1011.",
        "",
        "```",
        "./run.ps1 -Max -Slice e12 -o execution-fanout",
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
