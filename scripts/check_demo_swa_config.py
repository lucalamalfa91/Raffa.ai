#!/usr/bin/env python3
"""Check a deployed Static Web App's `/config.json` targets the right API
(ADR-012 "config, not code"; ADR-016 promotion; ADR-022 Day-1 demo auth).

Task E10/F03/US01/T01 (parent story us-01-demo-v-smoke, AC-2): "After
promote, `demo` SWA `/config.json` has the demo API URL and Entra public
client (not localhost, not `dev`)." `config.json` is a public,
unauthenticated static asset -- `web/public/staticwebapp.config.json`
explicitly excludes it from the SPA's navigation-fallback rewrite so it is
always served as plain JSON, never `index.html` -- written per-environment
by `.github/workflows/web.yml`'s "Write per-environment config.json" step
(`scripts/write_web_runtime_config.py`). This script is the read-side check
of that same contract, run *after* a promotion instead of during one.

Deliberately not a second promotion path (ADR-016's own "do not invent a
second promotion path"): it never deploys, never calls Azure/`az`, and
never needs a credential -- the caller (an operator, or the optional
`.github/workflows/demo-config-check.yml` workflow_dispatch job) supplies
the already-known Static Web App hostname, e.g. copied from the `web.yml`
deploy job's own "Project deployed to https://<host>" log line, or from
`az staticwebapp show --name swa-contigo-<env> --resource-group
rg-contigo-<env> --query defaultHostname`.

Verified live against the real `demo` and `dev` Static Web Apps on
2026-09-07/08 (see `.helix/reports/execution/demo-v-promotion-runbook.md`
for the recorded output this script's checks are modelled on):

    demo: https://mango-desert-084c2231e.6.azurestaticapps.net/config.json
      apiBaseUrl -> https://ca-contigo-demo-api.lemonsea-be9510a4.northeurope.azurecontainerapps.io
    dev:  https://mango-pond-061bc6d1e.6.azurestaticapps.net/config.json
      apiBaseUrl -> https://ca-contigo-dev-api.politetree-8bd9702e.northeurope.azurecontainerapps.io

Usage:
    python scripts/check_demo_swa_config.py --host mango-desert-084c2231e.6.azurestaticapps.net --environment demo
    python scripts/check_demo_swa_config.py --url https://mango-desert-084c2231e.6.azurestaticapps.net/config.json --environment demo
"""

from __future__ import annotations

import argparse
import json
import sys
import urllib.error
import urllib.request
from typing import Sequence

DEFAULT_TIMEOUT_SECONDS = 15.0
REQUIRED_STRING_FIELDS = ("apiBaseUrl", "oidcAuthority", "oidcClientId", "oidcRedirectUri")
# Same placeholder vocabulary scripts/write_web_runtime_config.py refuses to
# write -- if one of these ever leaked through to a live deploy, this check
# must fail it, not just the write-side guard.
FORBIDDEN_API_SUBSTRINGS = ("localhost", "127.0.0.1", "replace_with_")
ENVIRONMENTS = ("demo", "dev")


class CheckError(ValueError):
    """The fetched (or requested) config could not be checked at all --
    unreachable host, non-200, malformed JSON. Distinct from a *gap* (a
    reachable, valid config that still fails an AC-2 assertion) -- see
    `evaluate_config`."""


def config_url_from_host(host: str) -> str:
    """Bare-hostname -> full config.json URL. Rejects an accidental scheme
    so `--host` and `--url` cannot silently produce the same double-scheme
    mistake (e.g. `--host https://foo` -> `https://https://foo/config.json`)."""
    host = host.strip()
    if "://" in host:
        raise CheckError(f"--host must be a bare hostname (no scheme), got {host!r}; use --url instead")
    host = host.rstrip("/")
    if not host:
        raise CheckError("--host is empty")
    return f"https://{host}/config.json"


def other_environment(environment: str) -> str:
    if environment not in ENVIRONMENTS:
        raise CheckError(f"environment must be one of {ENVIRONMENTS}, got {environment!r}")
    return "dev" if environment == "demo" else "demo"


def evaluate_config(config: object, environment: str) -> list[str]:
    """Pure logic, no network -- unit-tested directly
    (tests/test_check_demo_swa_config.py), mirroring
    scripts/write_web_runtime_config.py's own pure-logic/network split.

    Returns a list of human-readable gap strings; an empty list means
    `config` passes every AC-2 assertion for `environment`.
    """
    if not isinstance(config, dict):
        return [f"config.json is not a JSON object, got {type(config).__name__}"]

    gaps: list[str] = []
    for field in REQUIRED_STRING_FIELDS:
        value = config.get(field)
        if not isinstance(value, str) or not value.strip():
            gaps.append(f"{field} is missing or empty")
    if gaps:
        # Cannot meaningfully inspect content below without the strings.
        return gaps

    other_env = other_environment(environment)
    api_base_url = config["apiBaseUrl"]
    api_base_url_lower = api_base_url.lower()
    this_marker = f"ca-contigo-{environment}-api"
    other_marker = f"ca-contigo-{other_env}-api"

    for needle in FORBIDDEN_API_SUBSTRINGS:
        if needle in api_base_url_lower:
            gaps.append(f"apiBaseUrl={api_base_url!r} contains forbidden placeholder {needle!r}")

    if other_marker in api_base_url_lower:
        gaps.append(
            f"apiBaseUrl={api_base_url!r} points at {other_env}'s API ({other_marker!r}), not {environment}'s"
        )
    elif this_marker not in api_base_url_lower:
        gaps.append(f"apiBaseUrl={api_base_url!r} does not contain the expected {this_marker!r} host segment")

    oidc_client_id = config["oidcClientId"].strip()
    if oidc_client_id.lower().startswith("replace_with"):
        gaps.append(f"oidcClientId={oidc_client_id!r} is still the local-dev placeholder")

    scopes = config.get("oidcApiScopes")
    if not isinstance(scopes, list) or not scopes or not all(isinstance(s, str) and s.strip() for s in scopes):
        gaps.append("oidcApiScopes must be a non-empty list of non-empty strings")
    else:
        scope_marker = f"contigo-{environment}-api"
        other_scope_marker = f"contigo-{other_env}-api"
        if any(other_scope_marker in s for s in scopes):
            gaps.append(f"oidcApiScopes {scopes!r} reference {other_scope_marker!r} ({other_env}), not {environment}")
        elif not any(scope_marker in s for s in scopes):
            gaps.append(f"oidcApiScopes {scopes!r} do not reference {scope_marker!r}")

    return gaps


def fetch_config(url: str, timeout: float = DEFAULT_TIMEOUT_SECONDS) -> object:
    """Network fetch -- not unit tested (mirrors write_web_runtime_config.py's
    own split: pure logic is tested, network/subprocess orchestration is
    proven live -- see the module docstring's recorded 2026-09-07/08 run)."""
    request = urllib.request.Request(url, headers={"Accept": "application/json"})
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:  # noqa: S310 (fixed https URL, GET only)
            status = getattr(response, "status", None) or response.getcode()
            body = response.read().decode("utf-8")
    except urllib.error.HTTPError as exc:
        raise CheckError(f"GET {url} returned HTTP {exc.code}") from exc
    except urllib.error.URLError as exc:
        raise CheckError(f"could not reach {url}: {exc.reason}") from exc
    if status != 200:
        raise CheckError(f"GET {url} returned HTTP {status}")
    try:
        return json.loads(body)
    except json.JSONDecodeError as exc:
        raise CheckError(f"{url} did not return valid JSON: {exc}") from exc


def _parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--host", help="Bare SWA hostname, e.g. mango-desert-084c2231e.6.azurestaticapps.net")
    source.add_argument("--url", help="Full config.json URL (use instead of --host, e.g. for a non-default path)")
    parser.add_argument(
        "--environment",
        choices=ENVIRONMENTS,
        default="demo",
        help="Which environment's API host config.json must reference (default: demo).",
    )
    parser.add_argument("--timeout", type=float, default=DEFAULT_TIMEOUT_SECONDS)
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    args = _parse_args(argv)
    try:
        url = args.url or config_url_from_host(args.host)
        config = fetch_config(url, timeout=args.timeout)
    except CheckError as exc:
        print(f"[FAIL] {exc}", file=sys.stderr)
        return 1

    gaps = evaluate_config(config, args.environment)
    if gaps:
        print(f"[FAIL] {url} ({args.environment}):", file=sys.stderr)
        for gap in gaps:
            print(f"  - {gap}", file=sys.stderr)
        return 1

    assert isinstance(config, dict)  # evaluate_config returned no gaps -> shape is proven
    print(
        f"[PASS] {url} config.json is a real, non-localhost {args.environment} config "
        f"(apiBaseUrl={config['apiBaseUrl']!r}, oidcClientId={config['oidcClientId']!r})"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
