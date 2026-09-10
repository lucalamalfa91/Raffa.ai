#!/usr/bin/env python3
"""Parse + reference-check a Helix artifact without launching a run.

    HelixDocument.model_validate(_expand_env(yaml.safe_load(...)))
    validate_references(doc)

Unset ${VAR} is a hard error unless --stub-env.

    python scripts/validate-artifact.py raffa-process.yaml
    python scripts/validate-artifact.py raffa-process.yaml --stub-env
"""

from __future__ import annotations

import argparse
import os
import re
import sys
from pathlib import Path
from typing import Any

_ENV_PATTERN = re.compile(r"\$\{([A-Za-z_][A-Za-z0-9_]*)\}")


def expand_env(obj: Any, *, stub: bool = False) -> Any:
    if isinstance(obj, dict):
        return {k: expand_env(v, stub=stub) for k, v in obj.items()}
    if isinstance(obj, list):
        return [expand_env(v, stub=stub) for v in obj]
    if isinstance(obj, str):
        missing: list[str] = []

        def _sub(match: re.Match[str]) -> str:
            name = match.group(1)
            value = os.environ.get(name)
            if value is None:
                if stub:
                    return f"stub-{name.lower().replace('_', '-')}"
                missing.append(name)
                return match.group(0)
            return value

        result = _ENV_PATTERN.sub(_sub, obj)
        if missing:
            raise RuntimeError(
                "unset environment variable(s) referenced in artifact: "
                + ", ".join(sorted(set(missing)))
            )
        return result
    return obj


def _find_backend(explicit: str | None) -> Path:
    candidates = []
    if explicit:
        candidates.append(Path(explicit))
    if os.environ.get("HELIX_BACKEND"):
        candidates.append(Path(os.environ["HELIX_BACKEND"]))
    here = Path(__file__).resolve()
    candidates.append(here.parents[3].parent / "helix" / "src" / "backend")
    for c in candidates:
        if (c / "helix" / "contract" / "schema.py").is_file():
            return c
    raise SystemExit(
        "ERROR: Helix backend not found. Pass --helix-backend <path> "
        "or export HELIX_BACKEND. Looked in: " + ", ".join(str(c) for c in candidates)
    )


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("artifact", help="path to the artifact YAML")
    ap.add_argument("--helix-backend", default=None)
    ap.add_argument("--stub-env", action="store_true")
    args = ap.parse_args()

    backend = _find_backend(args.helix_backend)
    sys.path.insert(0, str(backend))

    import yaml  # noqa: PLC0415

    from helix.contract.schema import HelixDocument  # noqa: PLC0415
    from helix.contract.validate import validate_references  # noqa: PLC0415

    artifact = Path(args.artifact).resolve()
    raw = yaml.safe_load(artifact.read_text(encoding="utf-8"))

    os.chdir(artifact.parent)

    doc = HelixDocument.model_validate(expand_env(raw, stub=args.stub_env))
    advisories = "ran"
    try:
        validate_references(doc)
    except ModuleNotFoundError as exc:
        if exc.name not in {"agent_framework"}:
            raise
        advisories = f"SKIPPED (missing module {exc.name}: non-blocking advisories)"

    print("OK", [o.id for o in doc.orchestrations])
    print("advisory ADR-0103:", advisories)

    missing = []
    for agent in doc.agents:
        if agent.instructions_file and not Path(agent.instructions_file).is_file():
            missing.append(f"agents[{agent.id}].instructions_file -> {agent.instructions_file}")
    for skill in doc.skills:
        if skill.instructions_file and not Path(skill.instructions_file).is_file():
            missing.append(f"skills[{skill.id}].instructions_file -> {skill.instructions_file}")
    if missing:
        print("\nMISSING PROMPT FILES (validate_references does not check these):")
        for m in missing:
            print("  -", m)
        return 1
    print("prompt files: all present")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
