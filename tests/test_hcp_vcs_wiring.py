"""Unit tests for scripts/hcp_vcs_wiring.py (task E01/F01/US02/T02).

Covers the pure assertion logic (assert_remote_execution_mode,
classify_vcs_wiring, evaluate_workspace) against synthetic HCP Terraform
workspace-attribute fixtures shaped like the real API response -- no
network, no token needed -- plus the local git-tracked-tfstate scan against
a throwaway repo and, finally, that same scan against this real working
tree.

main()'s live GET orchestration and its re-run of bootstrap_hcp_org.py need
a real TFE_TOKEN and the real `raffa-platform` HCP Terraform organization,
so that path is intentionally exercised live via
`python scripts/hcp_vcs_wiring.py [--check-only]`, not from this unit-test
file -- parity with scripts/bootstrap_hcp_org.py, whose main() is likewise
proven live rather than unit tested.

Run:
    python tests/test_hcp_vcs_wiring.py -v
"""

from __future__ import annotations

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO_ROOT / "scripts"))

import hcp_vcs_wiring as vcs  # noqa: E402

IDENTIFIER = "lucalamalfa91/raffa"


def _attrs(
    execution_mode: str = "remote",
    vcs_repo: dict | None = None,
    trigger_prefixes: list[str] | None = None,
    file_triggers_enabled: bool | None = None,
) -> dict:
    attrs: dict = {"execution-mode": execution_mode}
    if vcs_repo is not None:
        attrs["vcs-repo"] = vcs_repo
    if trigger_prefixes is not None:
        attrs["trigger-prefixes"] = trigger_prefixes
    if file_triggers_enabled is not None:
        attrs["file-triggers-enabled"] = file_triggers_enabled
    return attrs


class AssertRemoteExecutionModeTests(unittest.TestCase):
    def test_remote_passes(self) -> None:
        ok, detail = vcs.assert_remote_execution_mode(_attrs(execution_mode="remote"))
        self.assertTrue(ok, detail)

    def test_local_fails(self) -> None:
        ok, detail = vcs.assert_remote_execution_mode(_attrs(execution_mode="local"))
        self.assertFalse(ok)
        self.assertIn("local", detail)

    def test_agent_fails(self) -> None:
        ok, detail = vcs.assert_remote_execution_mode(_attrs(execution_mode="agent"))
        self.assertFalse(ok, detail)

    def test_missing_key_fails(self) -> None:
        ok, _detail = vcs.assert_remote_execution_mode({})
        self.assertFalse(ok)


class ClassifyVcsWiringTests(unittest.TestCase):
    def test_no_vcs_repo_is_pending(self) -> None:
        status, detail = vcs.classify_vcs_wiring(_attrs(), IDENTIFIER)
        self.assertEqual(status, "pending", detail)

    def test_correct_wiring_is_wired(self) -> None:
        attrs = _attrs(
            vcs_repo={"identifier": IDENTIFIER, "branch": "main"},
            trigger_prefixes=["infra/"],
            file_triggers_enabled=True,
        )
        status, detail = vcs.classify_vcs_wiring(attrs, IDENTIFIER)
        self.assertEqual(status, "wired", detail)

    def test_wrong_identifier_is_mismatched(self) -> None:
        attrs = _attrs(
            vcs_repo={"identifier": "someone-else/raffa", "branch": "main"},
            trigger_prefixes=["infra/"],
            file_triggers_enabled=True,
        )
        status, detail = vcs.classify_vcs_wiring(attrs, IDENTIFIER)
        self.assertEqual(status, "mismatched")
        self.assertIn("identifier", detail)

    def test_wrong_branch_is_mismatched(self) -> None:
        attrs = _attrs(
            vcs_repo={"identifier": IDENTIFIER, "branch": "develop"},
            trigger_prefixes=["infra/"],
            file_triggers_enabled=True,
        )
        status, detail = vcs.classify_vcs_wiring(attrs, IDENTIFIER)
        self.assertEqual(status, "mismatched")
        self.assertIn("branch", detail)

    def test_file_triggers_disabled_is_mismatched(self) -> None:
        attrs = _attrs(
            vcs_repo={"identifier": IDENTIFIER, "branch": "main"},
            trigger_prefixes=["infra/"],
            file_triggers_enabled=False,
        )
        status, detail = vcs.classify_vcs_wiring(attrs, IDENTIFIER)
        self.assertEqual(status, "mismatched")
        self.assertIn("file-triggers-enabled", detail)

    def test_missing_trigger_prefix_is_mismatched(self) -> None:
        attrs = _attrs(
            vcs_repo={"identifier": IDENTIFIER, "branch": "main"},
            trigger_prefixes=["web/"],
            file_triggers_enabled=True,
        )
        status, detail = vcs.classify_vcs_wiring(attrs, IDENTIFIER)
        self.assertEqual(status, "mismatched")
        self.assertIn("trigger-prefixes", detail)


class EvaluateWorkspaceTests(unittest.TestCase):
    def test_remote_and_pending_is_ok_overall(self) -> None:
        ok, lines = vcs.evaluate_workspace("raffa-dev", _attrs(execution_mode="remote"), IDENTIFIER)
        self.assertTrue(ok, lines)
        self.assertTrue(any("PASS" in l and "remote-execution-mode" in l for l in lines), lines)
        self.assertTrue(any("WARN" in l and "pending" in l for l in lines), lines)

    def test_remote_and_wired_is_ok_overall(self) -> None:
        attrs = _attrs(
            execution_mode="remote",
            vcs_repo={"identifier": IDENTIFIER, "branch": "main"},
            trigger_prefixes=["infra/"],
            file_triggers_enabled=True,
        )
        ok, lines = vcs.evaluate_workspace("raffa-dev", attrs, IDENTIFIER)
        self.assertTrue(ok, lines)
        self.assertTrue(any("PASS" in l and "wired" in l for l in lines), lines)

    def test_local_execution_mode_fails_overall_even_if_vcs_pending(self) -> None:
        ok, lines = vcs.evaluate_workspace("raffa-dev", _attrs(execution_mode="local"), IDENTIFIER)
        self.assertFalse(ok, lines)

    def test_mismatched_vcs_fails_overall_even_if_remote(self) -> None:
        attrs = _attrs(
            execution_mode="remote",
            vcs_repo={"identifier": "wrong/repo", "branch": "main"},
            trigger_prefixes=["infra/"],
            file_triggers_enabled=True,
        )
        ok, lines = vcs.evaluate_workspace("raffa-dev", attrs, IDENTIFIER)
        self.assertFalse(ok, lines)


class FindTrackedTfstatePathsTests(unittest.TestCase):
    def _init_repo(self, root: Path) -> None:
        for args in (
            ["init", "-q"],
            ["config", "user.email", "test@example.com"],
            ["config", "user.name", "test"],
        ):
            subprocess.run(["git", *args], cwd=root, check=True, capture_output=True, text=True)

    def _commit_all(self, root: Path) -> None:
        subprocess.run(["git", "add", "."], cwd=root, check=True, capture_output=True, text=True)
        subprocess.run(
            ["git", "commit", "-q", "-m", "init"], cwd=root, check=True, capture_output=True, text=True
        )

    def test_clean_repo_has_no_hits(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self._init_repo(root)
            (root / "main.tf").write_text("# no state here\n")
            self._commit_all(root)
            self.assertEqual(vcs.find_tracked_tfstate_paths(root), [])

    def test_committed_tfstate_is_flagged(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self._init_repo(root)
            (root / "terraform.tfstate").write_text("{}")
            self._commit_all(root)
            hits = vcs.find_tracked_tfstate_paths(root)
            self.assertIn("terraform.tfstate", hits)

    def test_dot_terraform_directory_is_flagged(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self._init_repo(root)
            nested = root / ".terraform" / "modules"
            nested.mkdir(parents=True)
            (nested / "modules.json").write_text("{}")
            self._commit_all(root)
            hits = vcs.find_tracked_tfstate_paths(root)
            self.assertTrue(any(".terraform" in h for h in hits), hits)

    def test_untracked_tfstate_is_not_flagged(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self._init_repo(root)
            (root / "README.md").write_text("clean\n")
            self._commit_all(root)
            (root / "leftover.tfstate").write_text("{}")
            self.assertEqual(vcs.find_tracked_tfstate_paths(root), [])


class AssertStateNotInGitTests(unittest.TestCase):
    def test_passes_on_clean_repo(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            for args in (
                ["init", "-q"],
                ["config", "user.email", "test@example.com"],
                ["config", "user.name", "test"],
            ):
                subprocess.run(["git", *args], cwd=root, check=True, capture_output=True, text=True)
            (root / "README.md").write_text("clean\n")
            subprocess.run(["git", "add", "."], cwd=root, check=True, capture_output=True, text=True)
            subprocess.run(
                ["git", "commit", "-q", "-m", "init"], cwd=root, check=True, capture_output=True, text=True
            )
            ok, detail = vcs.assert_state_not_in_git(root)
            self.assertTrue(ok, detail)

    def test_this_repo_has_no_tracked_tfstate(self) -> None:
        """Same invocation this task's own definition of done relies on."""
        ok, detail = vcs.assert_state_not_in_git(REPO_ROOT)
        self.assertTrue(ok, detail)


if __name__ == "__main__":
    unittest.main()
