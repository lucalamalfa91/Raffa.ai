"""Launch raffa-next-process.yaml without resuming a dead Helix checkpoint.

helix run defaults to continuation=auto, which resumes the newest unfinished
next-design checkpoint. The two W18 attempts inherited a w15 intake transcript
and then an empty nested stream. This wrapper is ContinuationSelection(mode=fresh).
"""
from __future__ import annotations

import os
import sys
from pathlib import Path

os.environ.setdefault("PYTHONUNBUFFERED", "1")
os.environ.setdefault("PYTHONUTF8", "1")
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

from helix.runner.continuation import ContinuationSelection
from helix.runner.launch import run_artifact_file

HERE = Path(__file__).resolve().parents[1]
ARTIFACT = HERE / "raffa-next-process.yaml"


def main() -> int:
    wave = os.environ.get("NEXT_WAVE", "w18")
    todo = os.environ.get("NEXT_TODO", "inputs/next/w18-todo.md")
    max_tasks = os.environ.get("NEXT_MAX_TASKS", "20")
    max_phases = os.environ.get("NEXT_MAX_PHASES", "5")
    previous = os.environ.get("NEXT_PREVIOUS", "w17")
    focus = os.environ.get("NEXT_FOCUS", "")
    orch = os.environ.get("NEXT_ORCH", "next-design")
    helix_input = (
        f"wave={wave} todo={todo} max_tasks={max_tasks} "
        f"max_phases={max_phases} previous={previous} focus={focus}"
    )
    print(
        f"artifact: {ARTIFACT.name}  orch: {orch}  wave: {wave}  "
        f"previous: {previous}  continuation: fresh",
        flush=True,
    )
    print(f"todo: {todo}  input: {helix_input}", flush=True)
    result = run_artifact_file(
        ARTIFACT,
        input=helix_input,
        orchestration=orch,
        working_dir=str(HERE),
        continuation=ContinuationSelection(mode="fresh"),
    )
    text = result.output or ""
    try:
        print(text, flush=True)
    except UnicodeEncodeError:
        sys.stdout.buffer.write((text + "\n").encode("utf-8", errors="replace"))
        sys.stdout.buffer.flush()
    if result.status != "completed":
        print(f"status={result.status} stop_reason={result.stop_reason}", flush=True)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
