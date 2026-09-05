#!/usr/bin/env python3
"""Cut wave-spec.schema.yaml into slices/e09.yaml only.

Never writes slice.current.yaml.
Never overwrites slices/e01.yaml–e08.yaml or wave-spec.execution.yaml.

Usage (cwd = .helix):
  python scripts/cut_schema_slices.py
"""

from __future__ import annotations

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE / "scripts"))

import cut_nightly_slices as base  # noqa: E402

MASTER = HERE / "reports" / "plan" / "wave-spec.schema.yaml"
OUT_DIR = HERE / "reports" / "plan" / "slices"
INDEX_SCHEMA = OUT_DIR / "INDEX-schema.md"
MANIFEST_SCHEMA = OUT_DIR / "MANIFEST-schema.yaml"
PROTECTED_SLICES = frozenset({f"e0{n}" for n in range(1, 9)})


def main() -> int:
    if not MASTER.exists():
        print(f"missing {MASTER}", file=sys.stderr)
        return 1
    text = MASTER.read_text(encoding="utf-8")
    if "waveId: placeholder" in text or "phases: []" in text:
        print("wave-spec.schema.yaml is still a placeholder; skip slice cut", file=sys.stderr)
        return 1
    rows = base.parse_master(text)
    live = [(ph, t) for ph, t in rows if t["status"] == "live"]
    if not live:
        print("no live tasks in wave-spec.schema.yaml", file=sys.stderr)
        return 1
    for _, t in live:
        epic = base.epic_id_of(t["id"])
        n = int(epic[1:])
        if n != 9:
            print(
                f"refusing to slice {t['id']}: schema cutter is E09 only",
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
        body = base.emit_yaml(sl.slice_id, sl.title, selected, sl.tokens)
        path = OUT_DIR / f"{sl.slice_id}.yaml"
        path.write_text(body, encoding="utf-8")
        written.append(sl.slice_id)

    lines = [
        "# Schema-apply slice (e09). Backend e01–e05 and web e06–e08 stay put.",
        "",
        "mode: schema-apply",
        "master: reports/plan/wave-spec.schema.yaml",
        "slices:",
    ]
    for sl in packed:
        lines.append(f"  - id: {sl.slice_id}")
        lines.append(f"    title: {sl.title}")
        lines.append(f"    tokens: {sl.tokens}")
        lines.append(f"    tasks: {len(sl.selected)}")
    MANIFEST_SCHEMA.write_text("\n".join(lines) + "\n", encoding="utf-8")

    idx = [
        "# Schema-apply slice (e09)",
        "",
        "Produced by `python scripts/cut_schema_slices.py`.",
        "Launch after e05 HITL (live process, not this YAML):",
        "",
        "```",
        "./run.ps1 -Max -Slice e09 -o execution-fanout",
        "```",
        "",
        "## Files",
        "",
    ]
    for sid in written:
        idx.append(f"- `{sid}.yaml`")
    INDEX_SCHEMA.write_text("\n".join(idx) + "\n", encoding="utf-8")
    print("wrote", ", ".join(written))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
