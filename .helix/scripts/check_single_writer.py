#!/usr/bin/env python3
"""Single-writer pre-flight for a slice: no two tasks of one phase may claim one file.

Reads a slice wave-spec (one flow-mapping task per line, as cut_*_slices.py writes
them), opens every live task's prompt file and parses its
``## Files to create or modify`` table. Two rules, both fail-closed:

1. **Same path, same phase.** A path (a backticked token of the Path column that
   has a directory component) claimed by two tasks of the same phase is a
   collision, whatever the Change column says: the phase barrier will have to
   merge two edits of one file.
2. **Creation counts as writing.** A file whose Change column says ``new`` may
   not be *named anywhere* in another task file of the same phase (its prose,
   its Context, its DoD). The name looked for is the path's last two components
   (``Raffa.Api/MarketEndpointExtensions.cs``) and, for the
   ``<X>EndpointExtensions.cs`` convention, the ``Map<X>Endpoints`` call a sibling
   would write into ``Program.cs``. e13 phase 3: F02/T02 created
   ``MarketEndpointExtensions.cs`` and F06/T01 mapped ``MapMarketEndpoints()``;
   the second task stubbed the file to compile, the barrier union-merged the two
   files and CI failed on 18 errors.

A bare file name with no directory (``ServiceCollectionExtensions.cs``) is not
comparable -- every module has one -- and is skipped. Glob claims
(``Application/Gate/*``) are compared verbatim, never expanded. Domain READMEs
are standing implementer scope (``skills/readme-hygiene.md``) and are ignored.

Usage (cwd = .helix):
  python scripts/check_single_writer.py --slice e13
  python scripts/check_single_writer.py            # slice.current.yaml (start hook)
  python scripts/check_single_writer.py --warn-only

Exit 0: no collision. Exit 1: at least one collision, or an unreadable slice /
task file (an unverifiable plan does not pass).
"""

from __future__ import annotations

import argparse
import re
import sys
from collections import defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
CURRENT = HERE / "reports" / "plan" / "slice.current.yaml"

_TASK_LINE_RE = re.compile(
    r"\bid:\s*['\"]?(?P<id>[A-Z]\d+/F\d+/US\d+/T\d+)['\"]?.*?\bprompt:\s*['\"]?(?P<prompt>[^,'\"}\s]+)"
)
_PHASE_RE = re.compile(r"^\s*-\s*id:\s*['\"]?(?P<phase>\d+)['\"]?\s*$")
_FILES_HEADING_RE = re.compile(r"^##\s+Files to create or modify", re.I)
_HEADING_RE = re.compile(r"^##\s+")
_BACKTICK_RE = re.compile(r"`([^`]+)`")
_README_RE = re.compile(r"(^|/)README\.md$", re.I)
_NEW_RE = re.compile(r"\bnew\b|\bcreate")
_ENDPOINT_SUFFIX = "EndpointExtensions"


def parse_slice(slice_file: Path) -> dict[str, list[tuple[str, str]]]:
    """``{phase_id: [(task_id, prompt_path), ...]}`` for live tasks."""
    phases: dict[str, list[tuple[str, str]]] = defaultdict(list)
    phase = "?"
    for raw in slice_file.read_text(encoding="utf-8").splitlines():
        pm = _PHASE_RE.match(raw)
        if pm:
            phase = pm.group("phase")
            continue
        tm = _TASK_LINE_RE.search(raw)
        if not tm:
            continue
        if not re.search(r"status:\s*['\"]?live", raw):
            continue
        phases[phase].append((tm.group("id"), tm.group("prompt")))
    return dict(phases)


def _split_paths(cell: str) -> list[str]:
    """Backticked tokens of a Path cell, split on commas, stripped, README-free."""
    out: list[str] = []
    for token in _BACKTICK_RE.findall(cell):
        for part in token.split(","):
            part = part.strip().strip("`").strip()
            if not part or _README_RE.search(part):
                continue
            out.append(part.replace("\\", "/"))
    return out


def parse_files_table(task_text: str) -> list[tuple[str, str]]:
    """``[(path, change), ...]`` from the ``## Files to create or modify`` table."""
    lines = task_text.splitlines()
    start = next((i for i, line in enumerate(lines) if _FILES_HEADING_RE.match(line)), None)
    if start is None:
        return []
    claims: list[tuple[str, str]] = []
    for line in lines[start + 1 :]:
        if _HEADING_RE.match(line):
            break
        if not line.strip().startswith("|"):
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if len(cells) < 2 or cells[0].lower() in {"path", "file"} or set(cells[0]) <= {"-", ":"}:
            continue
        change = cells[1].lower()
        for path in _split_paths(cells[0]):
            claims.append((path, change))
    return claims


def _is_new(change: str) -> bool:
    return bool(_NEW_RE.search(change))


def names_for(claimed: str) -> list[str]:
    """The strings that name a created file in a sibling task file: the path's last
    two components and, for ``<X>EndpointExtensions.cs``, the ``Map<X>Endpoints``
    call (F06/T01 named ``MapMarketEndpoints()``, never the file)."""
    parts = [p for p in claimed.split("/") if p]
    names = ["/".join(parts[-2:])]
    stem = Path(claimed).stem
    if stem.endswith(_ENDPOINT_SUFFIX) and len(stem) > len(_ENDPOINT_SUFFIX):
        names.append(f"Map{stem[: -len(_ENDPOINT_SUFFIX)]}Endpoints")
    return names


def check_slice_file(slice_file: Path) -> tuple[bool, str]:
    """Return ``(ok, detail)``; ``detail`` lists every collision when not ok."""
    if not slice_file.is_file():
        return False, f"slice file not found: {slice_file}"
    try:
        phases = parse_slice(slice_file)
    except OSError as exc:
        return False, f"cannot read {slice_file}: {exc}"
    if not phases:
        return False, f"no live tasks parsed from {slice_file.name}"

    problems: list[str] = []
    checked = 0
    for phase, tasks in sorted(phases.items(), key=lambda kv: kv[0]):
        claims: dict[str, list[tuple[str, str]]] = defaultdict(list)  # path -> [(task, change)]
        texts: dict[str, str] = {}
        for task_id, prompt in tasks:
            path = HERE / prompt
            if not path.is_file():
                problems.append(f"phase {phase}: {task_id} prompt missing: {prompt}")
                continue
            text = path.read_text(encoding="utf-8")
            texts[task_id] = text
            checked += 1
            for claimed, change in parse_files_table(text):
                claims[claimed].append((task_id, change))
        comparable = {c: o for c, o in claims.items() if "/" in c}
        # Rule 1 -- one path, two tasks, one phase.
        for claimed, owners in sorted(comparable.items()):
            distinct = sorted({t for t, _ in owners})
            if len(distinct) > 1:
                problems.append(
                    f"phase {phase}: `{claimed}` claimed by {', '.join(distinct)} "
                    "-- single writer per phase"
                )
        # Rule 2 -- a file created here is named by a sibling of the same phase.
        for claimed, owners in sorted(comparable.items()):
            creators = sorted({t for t, change in owners if _is_new(change)})
            name = Path(claimed).name
            if not creators or "*" in name or not name:
                continue
            for other_id, other_text in texts.items():
                if other_id in creators:
                    continue
                hit = next((n for n in names_for(claimed) if n in other_text), None)
                if hit is not None:
                    problems.append(
                        f"phase {phase}: `{claimed}` is created by {', '.join(creators)} and "
                        f"named by {other_id} (`{hit}`) in the same phase -- map or use it one "
                        "phase later"
                    )
    if problems:
        return False, "\n        ".join(problems)
    return True, f"{checked} task file(s) across {len(phases)} phase(s), no same-phase collision"


def main() -> int:
    parser = argparse.ArgumentParser(description="Same-phase single-writer check for a slice")
    parser.add_argument("--slice", help="slice id under reports/plan/slices/ (e.g. e13)")
    parser.add_argument("--file", help="explicit wave-spec path (artifact-relative or absolute)")
    parser.add_argument("--warn-only", action="store_true", help="report but exit 0")
    args = parser.parse_args()

    if args.file:
        slice_file = Path(args.file)
        if not slice_file.is_absolute():
            slice_file = HERE / slice_file
    elif args.slice:
        slice_file = HERE / "reports" / "plan" / "slices" / f"{args.slice.lower()}.yaml"
    else:
        slice_file = CURRENT

    ok, detail = check_slice_file(slice_file)
    label = "OK" if ok else "FAILED"
    print(f"SINGLE-WRITER {label}  {slice_file.name}", file=sys.stderr)
    print(f"        {detail}", file=sys.stderr)
    if ok or args.warn_only:
        return 0
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
