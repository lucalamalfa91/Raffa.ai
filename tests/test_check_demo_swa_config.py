"""Unit tests for scripts/check_demo_swa_config.py.

`.github/workflows/demo-config-check.yml` (and an operator, by hand) shell
out to this script after a `demo-v*` (or dev) promotion. These tests cover
the pure `evaluate_config`/`config_url_from_host`/`other_environment` logic
and the CLI wiring (with `fetch_config` mocked out) -- they do not call the
network. The real, live-fetched demo/dev payloads these fixtures are based
on are recorded in `.helix/reports/execution/demo-v-promotion-runbook.md`.

Run:
    python tests/test_check_demo_swa_config.py -v
"""

from __future__ import annotations

import io
import sys
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
from unittest.mock import patch

REPO_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO_ROOT / "scripts"))

import check_demo_swa_config as chk  # noqa: E402

# Real payloads fetched live from the demo and dev Static Web Apps on
# 2026-09-07/08 (see the runbook cited in the module docstring above).
DEMO_CONFIG = {
    "apiBaseUrl": "https://ca-contigo-demo-api.lemonsea-be9510a4.northeurope.azurecontainerapps.io",
    "oidcAuthority": "https://login.microsoftonline.com/248eb472-b4dc-401b-8c3b-44443f0e92a3",
    "oidcClientId": "85065229-1707-40b0-98ad-2b3d21db58cf",
    "oidcRedirectUri": "https://mango-desert-084c2231e.6.azurestaticapps.net/",
    "oidcApiScopes": [
        "api://contigo-demo-api/Contigo.Read",
        "api://contigo-demo-api/Contigo.Write",
    ],
}
DEV_CONFIG = {
    "apiBaseUrl": "https://ca-contigo-dev-api.politetree-8bd9702e.northeurope.azurecontainerapps.io",
    "oidcAuthority": "https://login.microsoftonline.com/248eb472-b4dc-401b-8c3b-44443f0e92a3",
    "oidcClientId": "da08e279-f6f4-4713-bee1-9dc70406e030",
    "oidcRedirectUri": "https://mango-pond-061bc6d1e.6.azurestaticapps.net/",
    "oidcApiScopes": [
        "api://contigo-dev-api/Contigo.Read",
        "api://contigo-dev-api/Contigo.Write",
    ],
}


class ConfigUrlFromHostTests(unittest.TestCase):
    def test_bare_host(self) -> None:
        self.assertEqual(
            chk.config_url_from_host("mango-desert-084c2231e.6.azurestaticapps.net"),
            "https://mango-desert-084c2231e.6.azurestaticapps.net/config.json",
        )

    def test_strips_trailing_slash(self) -> None:
        self.assertEqual(
            chk.config_url_from_host("example.azurestaticapps.net/"),
            "https://example.azurestaticapps.net/config.json",
        )

    def test_rejects_scheme(self) -> None:
        with self.assertRaises(chk.CheckError):
            chk.config_url_from_host("https://example.azurestaticapps.net")

    def test_rejects_empty(self) -> None:
        with self.assertRaises(chk.CheckError):
            chk.config_url_from_host("   ")


class OtherEnvironmentTests(unittest.TestCase):
    def test_demo_to_dev(self) -> None:
        self.assertEqual(chk.other_environment("demo"), "dev")

    def test_dev_to_demo(self) -> None:
        self.assertEqual(chk.other_environment("dev"), "demo")

    def test_rejects_unknown(self) -> None:
        with self.assertRaises(chk.CheckError):
            chk.other_environment("staging")


class EvaluateConfigTests(unittest.TestCase):
    def test_real_demo_config_passes_as_demo(self) -> None:
        self.assertEqual(chk.evaluate_config(DEMO_CONFIG, "demo"), [])

    def test_real_dev_config_passes_as_dev(self) -> None:
        self.assertEqual(chk.evaluate_config(DEV_CONFIG, "dev"), [])

    def test_demo_config_fails_as_dev(self) -> None:
        gaps = chk.evaluate_config(DEMO_CONFIG, "dev")
        self.assertTrue(gaps)
        self.assertTrue(any("demo" in gap and "apiBaseUrl" in gap for gap in gaps))

    def test_dev_config_fails_as_demo(self) -> None:
        gaps = chk.evaluate_config(DEV_CONFIG, "demo")
        self.assertTrue(gaps)
        self.assertTrue(any("dev" in gap and "apiBaseUrl" in gap for gap in gaps))

    def test_rejects_localhost(self) -> None:
        config = {**DEMO_CONFIG, "apiBaseUrl": "https://localhost:7109"}
        gaps = chk.evaluate_config(config, "demo")
        self.assertTrue(any("localhost" in gap for gap in gaps))

    def test_rejects_loopback_ip(self) -> None:
        config = {**DEMO_CONFIG, "apiBaseUrl": "https://127.0.0.1:7109"}
        gaps = chk.evaluate_config(config, "demo")
        self.assertTrue(any("127.0.0.1" in gap for gap in gaps))

    def test_rejects_replace_with_client_id(self) -> None:
        config = {**DEMO_CONFIG, "oidcClientId": "REPLACE_WITH_DEV_PUBLIC_CLIENT_ID"}
        gaps = chk.evaluate_config(config, "demo")
        self.assertTrue(any("placeholder" in gap for gap in gaps))

    def test_rejects_api_base_url_matching_neither_environment(self) -> None:
        config = {**DEMO_CONFIG, "apiBaseUrl": "https://api.example.com"}
        gaps = chk.evaluate_config(config, "demo")
        self.assertTrue(any("does not contain the expected" in gap for gap in gaps))

    def test_rejects_scopes_pointing_at_other_environment(self) -> None:
        # apiBaseUrl honestly says demo, but oidcApiScopes leaked dev's scopes
        # -- both must agree; this must still fail (defense in depth).
        config = {**DEMO_CONFIG, "oidcApiScopes": DEV_CONFIG["oidcApiScopes"]}
        gaps = chk.evaluate_config(config, "demo")
        self.assertTrue(any("oidcApiScopes" in gap for gap in gaps))

    def test_rejects_empty_scopes(self) -> None:
        config = {**DEMO_CONFIG, "oidcApiScopes": []}
        gaps = chk.evaluate_config(config, "demo")
        self.assertTrue(any("oidcApiScopes" in gap for gap in gaps))

    def test_rejects_missing_required_field(self) -> None:
        config = {k: v for k, v in DEMO_CONFIG.items() if k != "oidcAuthority"}
        gaps = chk.evaluate_config(config, "demo")
        self.assertEqual(gaps, ["oidcAuthority is missing or empty"])

    def test_rejects_non_dict(self) -> None:
        gaps = chk.evaluate_config(["not", "a", "dict"], "demo")
        self.assertTrue(any("not a JSON object" in gap for gap in gaps))


class MainCliTests(unittest.TestCase):
    def test_pass_prints_and_returns_zero(self) -> None:
        with patch.object(chk, "fetch_config", return_value=DEMO_CONFIG) as mock_fetch:
            out = io.StringIO()
            with redirect_stdout(out):
                rc = chk.main(["--host", "mango-desert-084c2231e.6.azurestaticapps.net", "--environment", "demo"])
        self.assertEqual(rc, 0)
        self.assertIn("[PASS]", out.getvalue())
        mock_fetch.assert_called_once()
        called_url = mock_fetch.call_args.args[0]
        self.assertEqual(called_url, "https://mango-desert-084c2231e.6.azurestaticapps.net/config.json")

    def test_fail_on_gap_returns_one(self) -> None:
        with patch.object(chk, "fetch_config", return_value=DEV_CONFIG):
            err = io.StringIO()
            with redirect_stderr(err):
                rc = chk.main(["--host", "mango-desert-084c2231e.6.azurestaticapps.net", "--environment", "demo"])
        self.assertEqual(rc, 1)
        self.assertIn("[FAIL]", err.getvalue())

    def test_unreachable_host_returns_one(self) -> None:
        with patch.object(chk, "fetch_config", side_effect=chk.CheckError("could not reach host")):
            err = io.StringIO()
            with redirect_stderr(err):
                rc = chk.main(["--host", "does-not-exist.azurestaticapps.net", "--environment", "demo"])
        self.assertEqual(rc, 1)
        self.assertIn("[FAIL]", err.getvalue())

    def test_url_overrides_host(self) -> None:
        with patch.object(chk, "fetch_config", return_value=DEMO_CONFIG) as mock_fetch:
            out = io.StringIO()
            with redirect_stdout(out):
                rc = chk.main(["--url", "https://custom.example/config.json", "--environment", "demo"])
        self.assertEqual(rc, 0)
        mock_fetch.assert_called_once()
        called_url = mock_fetch.call_args.args[0]
        self.assertEqual(called_url, "https://custom.example/config.json")


if __name__ == "__main__":
    unittest.main()
