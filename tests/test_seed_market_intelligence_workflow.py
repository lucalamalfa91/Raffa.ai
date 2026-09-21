"""Static checks for `.github/workflows/seed-market-intelligence.yml`.

The harness cannot execute a GitHub Actions runner, so these checks prove the
workflow's relevant structure directly from the repo text. This task's
regression is specifically that both `dotnet run ... ingest-market` invocations
must export the lazy blob-storage connection string the Worker host now
requires before it dispatches the command.

Run:
    python tests/test_seed_market_intelligence_workflow.py -v
"""

from __future__ import annotations

import re
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
WORKFLOW = REPO_ROOT / ".github" / "workflows" / "seed-market-intelligence.yml"


def _read(path: Path) -> str:
    if not path.is_file():
        raise AssertionError(f"{path} does not exist")
    return path.read_text(encoding="utf-8")


def _block_between(text: str, start_pattern: str, end_pattern: str | None) -> str:
    start = re.search(start_pattern, text)
    if start is None:
        raise AssertionError(f"pattern {start_pattern!r} not found")
    tail = text[start.end():]
    if end_pattern is None:
        return tail
    end = re.search(end_pattern, tail)
    return tail if end is None else tail[: end.start()]


class WorkflowFileTests(unittest.TestCase):
    def test_workflow_exists(self) -> None:
        self.assertTrue(WORKFLOW.is_file(), f"{WORKFLOW} does not exist")

    def test_workflow_name_matches_filename(self) -> None:
        text = _read(WORKFLOW)
        self.assertRegex(text, re.compile(r"^name:\s*seed-market-intelligence\s*$", re.MULTILINE))


class WorkerStorageConfigRegressionTests(unittest.TestCase):
    """The Worker host currently requires `ConnectionStrings:Storage` even for
    the one-shot `ingest-market` command, so both command steps must export the
    lazy dev-storage sentinel before `dotnet run`."""

    def setUp(self) -> None:
        text = _read(WORKFLOW)
        self.ingest_step = _block_between(
            text,
            r"Ingest market feed \(R-MKT-03\)[^\n]*\n",
            r"\n      - name: Re-run and assert idempotency \(R-MKT-03 AC-1\)\n",
        )
        self.idempotency_step = _block_between(
            text,
            r"Re-run and assert idempotency \(R-MKT-03 AC-1\)[^\n]*\n",
            r"\n      - name: Verify the market corpus \(R-MKT-02, R-MKT-03 AC-2\)\n",
        )

    def test_first_ingest_run_exports_storage_connection_string(self) -> None:
        self.assertIn('export ConnectionStrings__Storage="UseDevelopmentStorage=true"', self.ingest_step)
        self.assertIn('dotnet run --project backend/src/Raffa.Worker/Raffa.Worker.csproj', self.ingest_step)

    def test_second_ingest_run_exports_storage_connection_string(self) -> None:
        self.assertIn('export ConnectionStrings__Storage="UseDevelopmentStorage=true"', self.idempotency_step)
        self.assertIn('dotnet run --project backend/src/Raffa.Worker/Raffa.Worker.csproj', self.idempotency_step)


if __name__ == "__main__":
    unittest.main()
