#!/usr/bin/env python3
"""Write the SPA runtime config.json the web deploy job overlays onto web-dist.

ADR-012: one compiled bundle, per-environment values at deploy time (not
Vite VITE_* statics). Values are non-secret (PKCE public client, ADR-010).
Azure lookups stay in .github/workflows/web.yml; this script only shapes
and validates the JSON so a placeholder localhost config cannot ship.

Usage:
    python scripts/write_web_runtime_config.py \\
      --out web/dist/config.json \\
      --api-base-url https://ca-raffa-dev-api.example.azurecontainerapps.io \\
      --oidc-authority https://login.microsoftonline.com/<tenant-id> \\
      --oidc-client-id <public-client-app-id> \\
      --oidc-redirect-uri https://<swa-host>/ \\
      --oidc-api-scope api://raffa-dev-api/Raffa.Read \\
      --oidc-api-scope api://raffa-dev-api/Raffa.Write
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Sequence

FORBIDDEN_SUBSTRINGS = ("localhost", "127.0.0.1", "replace_with_")
GUID_RE = re.compile(
    r"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"
)
ORIGIN_ONLY_HTTPS = re.compile(r"^https://[^/]+/?$")


class ConfigError(ValueError):
    """Malformed or placeholder runtime config — fail the deploy, never guess."""


def _reject_placeholder(label: str, value: str) -> None:
    lowered = value.lower()
    for needle in FORBIDDEN_SUBSTRINGS:
        if needle in lowered:
            raise ConfigError(
                f"{label}={value!r} still looks like the local-dev placeholder; "
                "refusing to write it into the deploy artifact."
            )


def build_runtime_config(
    *,
    api_base_url: str,
    oidc_authority: str,
    oidc_client_id: str,
    oidc_redirect_uri: str,
    oidc_api_scopes: Sequence[str],
) -> dict:
    api_base_url = api_base_url.strip().rstrip("/")
    oidc_authority = oidc_authority.strip().rstrip("/")
    oidc_client_id = oidc_client_id.strip()
    oidc_redirect_uri = oidc_redirect_uri.strip()
    scopes = [scope.strip() for scope in oidc_api_scopes]

    if ORIGIN_ONLY_HTTPS.match(oidc_redirect_uri):
        oidc_redirect_uri = oidc_redirect_uri.rstrip("/") + "/"

    required = {
        "apiBaseUrl": api_base_url,
        "oidcAuthority": oidc_authority,
        "oidcClientId": oidc_client_id,
        "oidcRedirectUri": oidc_redirect_uri,
    }
    for label, value in required.items():
        if not value:
            raise ConfigError(f"{label} is empty")
        _reject_placeholder(label, value)

    if not api_base_url.startswith("https://"):
        raise ConfigError(f"apiBaseUrl must be https, got {api_base_url!r}")
    if not oidc_authority.startswith("https://login.microsoftonline.com/"):
        raise ConfigError(
            f"oidcAuthority must be https://login.microsoftonline.com/<tenant>, got {oidc_authority!r}"
        )
    if not GUID_RE.match(oidc_client_id):
        raise ConfigError(f"oidcClientId is not a GUID, got {oidc_client_id!r}")
    if not oidc_redirect_uri.startswith("https://"):
        raise ConfigError(f"oidcRedirectUri must be https, got {oidc_redirect_uri!r}")
    if ORIGIN_ONLY_HTTPS.match(oidc_redirect_uri.rstrip("/")) and not oidc_redirect_uri.endswith("/"):
        raise ConfigError(
            f"oidcRedirectUri origin-only values need a trailing slash, got {oidc_redirect_uri!r}"
        )
    if not scopes or any(not scope for scope in scopes):
        raise ConfigError("oidcApiScopes must be a non-empty list of non-empty strings")
    for scope in scopes:
        _reject_placeholder("oidcApiScopes", scope)

    return {
        "apiBaseUrl": api_base_url,
        "oidcAuthority": oidc_authority,
        "oidcClientId": oidc_client_id,
        "oidcRedirectUri": oidc_redirect_uri,
        "oidcApiScopes": list(scopes),
    }


def write_runtime_config(path: Path, config: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(config, indent=2) + "\n", encoding="utf-8")


def _parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--api-base-url", required=True)
    parser.add_argument("--oidc-authority", required=True)
    parser.add_argument("--oidc-client-id", required=True)
    parser.add_argument("--oidc-redirect-uri", required=True)
    parser.add_argument(
        "--oidc-api-scope",
        dest="oidc_api_scopes",
        action="append",
        required=True,
        help="Repeat for each API scope (e.g. api://raffa-dev-api/Raffa.Read).",
    )
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    args = _parse_args(argv)
    try:
        config = build_runtime_config(
            api_base_url=args.api_base_url,
            oidc_authority=args.oidc_authority,
            oidc_client_id=args.oidc_client_id,
            oidc_redirect_uri=args.oidc_redirect_uri,
            oidc_api_scopes=args.oidc_api_scopes,
        )
    except ConfigError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1
    write_runtime_config(args.out, config)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
