#!/usr/bin/env python3
"""Create HCP Terraform org raffa-platform if missing. Never prints tokens."""

from __future__ import annotations

import json
import os
import re
import ssl
import sys
import urllib.error
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
ORG = os.environ.get("RAFFA_TFC_ORG", "raffa-platform")
API = "https://app.terraform.io/api/v2"


def _load_dotenv(path: Path) -> None:
    if not path.is_file():
        return
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        name, value = line.split("=", 1)
        name, value = name.strip(), value.strip()
        if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
            value = value[1:-1]
        if not re.match(r"^[A-Za-z_][A-Za-z0-9_]*$", name):
            continue
        if os.environ.get(name):
            continue
        os.environ[name] = value


def _token() -> str:
    for var in ("TFE_TOKEN", "TF_TOKEN", "TF_API_TOKEN"):
        v = (os.environ.get(var) or "").strip()
        if v:
            return v
    cred = Path(os.environ.get("APPDATA") or "") / "terraform.d" / "credentials.tfrc.json"
    if not cred.is_file():
        cred = Path.home() / ".terraform.d" / "credentials.tfrc.json"
    if cred.is_file():
        data = json.loads(cred.read_text(encoding="utf-8"))
        hosts = data.get("credentials") or {}
        host = hosts.get("app.terraform.io") or {}
        tok = (host.get("token") or "").strip()
        if tok:
            return tok
    raise SystemExit("ERROR: no TFE_TOKEN/TF_TOKEN and no terraform login credentials")


def _req(method: str, path: str, payload: dict | None = None) -> tuple[int, dict | list | None, str]:
    body = None if payload is None else json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        API + path,
        data=body,
        method=method,
        headers={
            "Authorization": "Bearer " + _token(),
            "Content-Type": "application/vnd.api+json",
        },
    )
    ctx = ssl.create_default_context()
    try:
        with urllib.request.urlopen(req, context=ctx, timeout=45) as resp:
            raw = resp.read().decode("utf-8")
            return resp.status, json.loads(raw) if raw else None, ""
    except urllib.error.HTTPError as exc:
        raw = exc.read().decode("utf-8", errors="replace")
        parsed: dict | list | None
        try:
            parsed = json.loads(raw) if raw else None
        except json.JSONDecodeError:
            parsed = None
        return exc.code, parsed, raw[:500]


def main() -> int:
    os.chdir(HERE)
    _load_dotenv(HERE / ".env")
    status, data, raw = _req("GET", "/account/details")
    if status != 200 or not isinstance(data, dict):
        print(f"ERROR: HCP account/details HTTP {status}")
        print(raw or data)
        return 1
    attrs = (data.get("data") or {}).get("attributes") or {}
    email = attrs.get("email") or ""
    username = attrs.get("username") or ""
    print(f"HCP user: {username}")

    status, data, raw = _req("GET", "/organizations")
    if status != 200 or not isinstance(data, dict):
        print(f"ERROR: list organizations HTTP {status}")
        print(raw or data)
        return 1
    names = [((o.get("attributes") or {}).get("name") or "") for o in (data.get("data") or [])]
    print("existing orgs:", ", ".join(n for n in names if n) or "(none)")
    if ORG in names:
        print(f"org {ORG} already exists")
        return 0
    if not email:
        print("ERROR: account has no email; cannot create organization")
        return 1
    status, data, raw = _req(
        "POST",
        "/organizations",
        {
            "data": {
                "type": "organizations",
                "attributes": {"name": ORG, "email": email},
            }
        },
    )
    if status in (200, 201):
        print(f"created org {ORG}")
        return 0
    print(f"ERROR: create org HTTP {status}")
    print(raw or data)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
