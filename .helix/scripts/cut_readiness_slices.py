#!/usr/bin/env python3
"""Cut wave-spec.readiness.yaml into slices/e10.yaml only.

Never writes slice.current.yaml.
Never overwrites slices/e01.yaml–e09.yaml or wave-spec.execution.yaml.

Usage (cwd = .helix):
  python scripts/cut_readiness_slices.py
"""

from __future__ import annotations

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE / "scripts"))

import cut_nightly_slices as base  # noqa: E402

MASTER = HERE / "reports" / "plan" / "wave-spec.readiness.yaml"
OUT_DIR = HERE / "reports" / "plan" / "slices"
INDEX_READINESS = OUT_DIR / "INDEX-readiness.md"
MANIFEST_READINESS = OUT_DIR / "MANIFEST-readiness.yaml"
PROTECTED_SLICES = frozenset({f"e0{n}" for n in range(1, 10)})


def main() -> int:
    if not MASTER.exists():
        print(f"missing {MASTER}", file=sys.stderr)
        return 1
    text = MASTER.read_text(encoding="utf-8")
    if "waveId: placeholder" in text or "phases: []" in text:
        print("wave-spec.readiness.yaml is still a placeholder; skip slice cut", file=sys.stderr)
        return 1
    rows = base.parse_master(text)
    live = [(ph, t) for ph, t in rows if t["status"] == "live"]
    if not live:
        print("no live tasks in wave-spec.readiness.yaml", file=sys.stderr)
        return 1
    for _, t in live:
        epic = base.epic_id_of(t["id"])
        n = int(epic[1:])
        if n != 10:
            print(
                f"refusing to slice {t['id']}: readiness cutter is E10 only",
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
        "# Demo-readiness slice (e10). e01–e09 stay put.",
        "",
        "mode: demo-readiness",
        "master: reports/plan/wave-spec.readiness.yaml",
        "slices:",
    ]
    for sl in packed:
        lines.append(f"  - id: {sl.slice_id}")
        lines.append(f"    title: {sl.title}")
        lines.append(f"    tokens: {sl.tokens}")
        lines.append(f"    tasks: {len(sl.selected)}")
    MANIFEST_READINESS.write_text("\n".join(lines) + "\n", encoding="utf-8")

    idx = [
        "# Demo-readiness slice (e10)",
        "",
        "Produced by `python scripts/cut_readiness_slices.py`.",
        "Launch only after gap-report HITL **and** e09 + e06–e08 are closed",
        "(live process, not this YAML):",
        "",
        "```",
        "./run.ps1 -Max -Slice e10 -o execution-fanout",
        "```",
        "",
        "## Files",
        "",
    ]
    for sid in written:
        idx.append(f"- `{sid}.yaml`")
    INDEX_READINESS.write_text("\n".join(idx) + "\n", encoding="utf-8")
    print("wrote", ", ".join(written))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
