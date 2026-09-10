"""Unit tests for scripts/write_web_runtime_config.py.

The deploy job in web.yml shells out to this script after Azure lookups.
These tests cover the JSON shape and the placeholder-rejection guard so a
localhost config.json cannot ship; they do not call Azure.

Run:
    python tests/test_write_web_runtime_config.py -v
"""

from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO_ROOT / "scripts"))

import write_web_runtime_config as wrc  # noqa: E402

VALID = dict(
    api_base_url="https://ca-raffa-dev-api.example.azurecontainerapps.io",
    oidc_authority="https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000",
    oidc_client_id="11111111-1111-1111-1111-111111111111",
    oidc_redirect_uri="https://web.dev.raffa.example/",
    oidc_api_scopes=(
        "api://raffa-dev-api/Raffa.Read",
        "api://raffa-dev-api/Raffa.Write",
    ),
)


class BuildRuntimeConfigTests(unittest.TestCase):
    def test_happy_path_shape(self) -> None:
        config = wrc.build_runtime_config(**VALID)
        self.assertEqual(config["apiBaseUrl"], VALID["api_base_url"])
        self.assertEqual(config["oidcAuthority"], VALID["oidc_authority"])
        self.assertEqual(config["oidcClientId"], VALID["oidc_client_id"])
        self.assertEqual(config["oidcRedirectUri"], VALID["oidc_redirect_uri"])
        self.assertEqual(list(config["oidcApiScopes"]), list(VALID["oidc_api_scopes"]))

    def test_strips_trailing_slash_from_api_base_url(self) -> None:
        config = wrc.build_runtime_config(
            **{**VALID, "api_base_url": VALID["api_base_url"] + "/"}
        )
        self.assertEqual(config["apiBaseUrl"], VALID["api_base_url"])

    def test_adds_trailing_slash_on_origin_only_redirect(self) -> None:
        config = wrc.build_runtime_config(
            **{
                **VALID,
                "oidc_redirect_uri": "https://web.dev.raffa.example",
            }
        )
        self.assertEqual(config["oidcRedirectUri"], "https://web.dev.raffa.example/")

    def test_rejects_localhost_placeholder(self) -> None:
        with self.assertRaises(wrc.ConfigError):
            wrc.build_runtime_config(
                **{**VALID, "api_base_url": "https://localhost:7109"}
            )

    def test_rejects_replace_with_placeholder(self) -> None:
        with self.assertRaises(wrc.ConfigError):
            wrc.build_runtime_config(
                **{**VALID, "oidc_client_id": "REPLACE_WITH_DEV_PUBLIC_CLIENT_ID"}
            )

    def test_rejects_http_api(self) -> None:
        with self.assertRaises(wrc.ConfigError):
            wrc.build_runtime_config(
                **{**VALID, "api_base_url": "http://ca-raffa-dev-api.example"}
            )

    def test_rejects_non_guid_client_id(self) -> None:
        with self.assertRaises(wrc.ConfigError):
            wrc.build_runtime_config(**{**VALID, "oidc_client_id": "not-a-guid"})

    def test_rejects_empty_scopes(self) -> None:
        with self.assertRaises(wrc.ConfigError):
            wrc.build_runtime_config(**{**VALID, "oidc_api_scopes": ()})


class WriteAndCliTests(unittest.TestCase):
    def test_write_runtime_config_round_trip(self) -> None:
        config = wrc.build_runtime_config(**VALID)
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "dist" / "config.json"
            wrc.write_runtime_config(path, config)
            loaded = json.loads(path.read_text(encoding="utf-8"))
            self.assertEqual(loaded, config)

    def test_main_writes_file(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "config.json"
            rc = wrc.main(
                [
                    "--out",
                    str(path),
                    "--api-base-url",
                    VALID["api_base_url"],
                    "--oidc-authority",
                    VALID["oidc_authority"],
                    "--oidc-client-id",
                    VALID["oidc_client_id"],
                    "--oidc-redirect-uri",
                    VALID["oidc_redirect_uri"],
                    "--oidc-api-scope",
                    VALID["oidc_api_scopes"][0],
                    "--oidc-api-scope",
                    VALID["oidc_api_scopes"][1],
                ]
            )
            self.assertEqual(rc, 0)
            loaded = json.loads(path.read_text(encoding="utf-8"))
            self.assertEqual(loaded["oidcClientId"], VALID["oidc_client_id"])

    def test_main_rejects_placeholder(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "config.json"
            rc = wrc.main(
                [
                    "--out",
                    str(path),
                    "--api-base-url",
                    "https://localhost:7109",
                    "--oidc-authority",
                    VALID["oidc_authority"],
                    "--oidc-client-id",
                    VALID["oidc_client_id"],
                    "--oidc-redirect-uri",
                    VALID["oidc_redirect_uri"],
                    "--oidc-api-scope",
                    VALID["oidc_api_scopes"][0],
                ]
            )
            self.assertEqual(rc, 1)
            self.assertFalse(path.exists())


if __name__ == "__main__":
    unittest.main()
