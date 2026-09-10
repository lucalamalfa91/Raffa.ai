"""Unit checks for merge_verify skip prefixes (C2.1 / C1.4)."""

from __future__ import annotations

from pathlib import Path

import pytest

import merge_verify


def test_skips_helix_agent_docs() -> None:
    assert merge_verify._is_excluded(".helix/agents/conflict-fixer.md")
    assert merge_verify._is_excluded(".helix/scripts/merge_verify.py")
    assert merge_verify._is_excluded(".helix/skills/marker-discipline.md")
    assert merge_verify._is_excluded(".helix\\agents\\conflict-fixer.md")
    assert not merge_verify._is_excluded("backend/src/Raffa.SharedKernel/SystemClock.cs")
    assert not merge_verify._is_excluded(".helix/reports/open-questions.md")


def test_merge_verify_exclude_env(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("MERGE_VERIFY_EXCLUDE", "docs/examples/\n")
    assert merge_verify._is_excluded("docs/examples/conflict.md")
    assert not merge_verify._is_excluded("docs/other.md")


def test_needs_dotnet_build_only_for_backend_sources() -> None:
    assert merge_verify.needs_dotnet_build(["backend/src/Raffa.Api/MarketEndpointExtensions.cs"])
    assert merge_verify.needs_dotnet_build(["backend\\src\\Raffa.Market\\Raffa.Market.csproj"])
    assert not merge_verify.needs_dotnet_build(["backend/README.md", "web/src/app.tsx"])
    assert not merge_verify.needs_dotnet_build([".helix/reports/open-questions.md"])
    assert not merge_verify.needs_dotnet_build([])


def test_dotnet_build_skipped_by_env(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("MERGE_VERIFY_SKIP_BUILD", "1")
    assert merge_verify.dotnet_build(tmp_path) == 0


def test_files_with_markers_ignores_skipped(tmp_path: Path) -> None:
    agents = tmp_path / ".helix" / "agents"
    agents.mkdir(parents=True)
    (agents / "conflict-fixer.md").write_text("example <<<<<<< HEAD in docs\n")
    backend = tmp_path / "backend"
    backend.mkdir()
    (backend / "ok.cs").write_text("class X {}\n")
    dirty = merge_verify.files_with_markers(
        tmp_path,
        [".helix/agents/conflict-fixer.md", "backend/ok.cs"],
    )
    assert dirty == []
