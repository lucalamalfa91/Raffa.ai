"""Unit tests for scripts/apply_demo_environment_reviewers.py (task
E01/F03/US03/T02).

Covers the pure logic (build_desired_state, find_required_reviewers_rule,
extract_required_reviewers, describe_gaps) against synthetic Environments-
API-shaped fixtures, including one literal fixture captured live from
`gh api repos/lucalamalfa91/raffa/environments/demo` on 2026-09-03 (see
the script's own module docstring) -- no network, no token needed.

main()'s live PUT/GET orchestration (and resolve_user_id's live GET) needs a
real `gh` auth session against the real lucalamalfa91/raffa repo, so that
path is intentionally exercised live via
`python scripts/apply_demo_environment_reviewers.py [--check-only]`, not
from this unit-test file -- parity with tests/test_hcp_vcs_wiring.py and
tests/test_repo_secret_scan.py-style scripts in this repo.

Run:
    python tests/test_apply_demo_environment_reviewers.py -v
"""

from __future__ import annotations

import sys
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO_ROOT / "scripts"))

import apply_demo_environment_reviewers as ader  # noqa: E402

LUCA_ID = 57912352

# Captured live via `gh api repos/lucalamalfa91/raffa/environments/demo`
# on 2026-09-03, right after this script's own `apply_environment` first
# created the environment -- trimmed to the fields this module reads.
LIVE_DEMO_ENVIRONMENT_FIXTURE: dict = {
    "id": 21150697674,
    "name": "demo",
    "can_admins_bypass": True,
    "protection_rules": [
        {
            "id": 64501001,
            "type": "required_reviewers",
            "prevent_self_review": False,
            "reviewers": [
                {
                    "type": "User",
                    "reviewer": {"login": "lucalamalfa91", "id": LUCA_ID},
                }
            ],
        }
    ],
    "deployment_branch_policy": None,
}


class BuildDesiredStateTests(unittest.TestCase):
    def test_single_reviewer_shape(self) -> None:
        desired = ader.build_desired_state([LUCA_ID])
        self.assertEqual(desired["prevent_self_review"], False)
        self.assertEqual(desired["reviewers"], [{"type": "User", "id": LUCA_ID}])
        self.assertIsNone(desired["deployment_branch_policy"])

    def test_multiple_reviewers_preserve_order(self) -> None:
        desired = ader.build_desired_state([1, 2, 3])
        self.assertEqual(
            desired["reviewers"],
            [{"type": "User", "id": 1}, {"type": "User", "id": 2}, {"type": "User", "id": 3}],
        )

    def test_no_reviewers_is_empty_list_not_missing_key(self) -> None:
        desired = ader.build_desired_state([])
        self.assertEqual(desired["reviewers"], [])


class FindRequiredReviewersRuleTests(unittest.TestCase):
    def test_found_in_live_fixture(self) -> None:
        rule = ader.find_required_reviewers_rule(LIVE_DEMO_ENVIRONMENT_FIXTURE)
        self.assertIsNotNone(rule)
        assert rule is not None
        self.assertEqual(rule["type"], "required_reviewers")

    def test_missing_when_no_protection_rules(self) -> None:
        self.assertIsNone(ader.find_required_reviewers_rule({"name": "demo"}))

    def test_missing_when_only_other_rule_types(self) -> None:
        state = {
            "protection_rules": [
                {"type": "wait_timer", "wait_timer": 30},
                {"type": "branch_policy"},
            ]
        }
        self.assertIsNone(ader.find_required_reviewers_rule(state))

    def test_found_among_other_rule_types(self) -> None:
        state = {
            "protection_rules": [
                {"type": "wait_timer", "wait_timer": 30},
                {
                    "type": "required_reviewers",
                    "prevent_self_review": False,
                    "reviewers": [{"type": "User", "reviewer": {"id": LUCA_ID}}],
                },
                {"type": "branch_policy"},
            ]
        }
        rule = ader.find_required_reviewers_rule(state)
        self.assertIsNotNone(rule)


class ExtractRequiredReviewersTests(unittest.TestCase):
    def test_live_fixture_yields_one_user_entry(self) -> None:
        entries = ader.extract_required_reviewers(LIVE_DEMO_ENVIRONMENT_FIXTURE)
        self.assertEqual(entries, [("User", LUCA_ID)])

    def test_no_rule_yields_empty(self) -> None:
        self.assertEqual(ader.extract_required_reviewers({"name": "demo"}), [])

    def test_empty_reviewers_list_yields_empty(self) -> None:
        state = {"protection_rules": [{"type": "required_reviewers", "reviewers": []}]}
        self.assertEqual(ader.extract_required_reviewers(state), [])

    def test_team_entry_is_preserved_with_its_type(self) -> None:
        state = {
            "protection_rules": [
                {
                    "type": "required_reviewers",
                    "reviewers": [
                        {"type": "User", "reviewer": {"id": LUCA_ID}},
                        {"type": "Team", "reviewer": {"id": 999}},
                    ],
                }
            ]
        }
        entries = ader.extract_required_reviewers(state)
        self.assertCountEqual(entries, [("User", LUCA_ID), ("Team", 999)])


class DescribeGapsTests(unittest.TestCase):
    def test_live_fixture_matches_expected_single_reviewer(self) -> None:
        gaps = ader.describe_gaps(LIVE_DEMO_ENVIRONMENT_FIXTURE, [LUCA_ID])
        self.assertEqual(gaps, [], gaps)

    def test_no_protection_rule_is_a_gap(self) -> None:
        gaps = ader.describe_gaps({"name": "demo"}, [LUCA_ID])
        self.assertEqual(len(gaps), 1)
        self.assertIn("no required_reviewers protection rule", gaps[0])

    def test_missing_expected_reviewer_is_a_gap(self) -> None:
        state = {
            "name": "demo",
            "protection_rules": [
                {"type": "required_reviewers", "prevent_self_review": False, "reviewers": []}
            ],
        }
        gaps = ader.describe_gaps(state, [LUCA_ID])
        self.assertEqual(len(gaps), 1)
        self.assertIn("required reviewers", gaps[0])
        self.assertIn("missing", gaps[0])

    def test_unexpected_extra_reviewer_is_a_gap(self) -> None:
        state = {
            "name": "demo",
            "protection_rules": [
                {
                    "type": "required_reviewers",
                    "prevent_self_review": False,
                    "reviewers": [
                        {"type": "User", "reviewer": {"id": LUCA_ID}},
                        {"type": "User", "reviewer": {"id": 4242}},
                    ],
                }
            ],
        }
        gaps = ader.describe_gaps(state, [LUCA_ID])
        self.assertEqual(len(gaps), 1)
        self.assertIn("unexpected", gaps[0])

    def test_prevent_self_review_true_is_a_gap(self) -> None:
        state = {
            "name": "demo",
            "protection_rules": [
                {
                    "type": "required_reviewers",
                    "prevent_self_review": True,
                    "reviewers": [{"type": "User", "reviewer": {"id": LUCA_ID}}],
                }
            ],
        }
        gaps = ader.describe_gaps(state, [LUCA_ID])
        self.assertEqual(len(gaps), 1)
        self.assertIn("prevent_self_review", gaps[0])

    def test_wrong_reviewer_and_prevent_self_review_both_reported(self) -> None:
        state = {
            "name": "demo",
            "protection_rules": [
                {
                    "type": "required_reviewers",
                    "prevent_self_review": True,
                    "reviewers": [{"type": "User", "reviewer": {"id": 4242}}],
                }
            ],
        }
        gaps = ader.describe_gaps(state, [LUCA_ID])
        self.assertEqual(len(gaps), 2)


if __name__ == "__main__":
    unittest.main()
