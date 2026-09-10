#!/usr/bin/env python3
"""Verify the Raffa product remote https://github.com/lucalamalfa91/raffa.

Task E01/F01/US01/T01: the repo already exists under the lucalamalfa91 account
and is **public**. This script asserts owner, name, visibility=public,
description "Raffa platform", and default branch `main`. It does not create
a GitHub organization, does not require a four-repo org, and does not fail
on other repos owned by the same user.

Owner/repo resolve from RAFFA_GITHUB_OWNER / RAFFA_GITHUB_REPO
(defaults lucalamalfa91 / raffa). RAFFA_GITHUB_ORG is accepted as an
alias for the owner. Authenticates via whatever `gh` already has configured.

Read-only by default. Pass --create-missing to create the single public repo
if it is absent (existing repos are never recreated).

Usage:
  python scripts/verify_github_repos.py
  python scripts/verify_github_repos.py --create-missing

Exit 0 only if lucalamalfa91/raffa exists, is public, default_branch=main,
and description is Raffa platform. Exit 1 otherwise.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess

OWNER_ENV = "RAFFA_GITHUB_OWNER"
ORG_ENV = "RAFFA_GITHUB_ORG"
REPO_ENV = "RAFFA_GITHUB_REPO"
DEFAULT_OWNER = "lucalamalfa91"
DEFAULT_REPO = "raffa"
EXPECTED_DESCRIPTION = "Raffa platform"
EXPECTED_VISIBILITY = "public"


def _owner() -> str:
    return (
        (os.environ.get(OWNER_ENV) or "").strip()
        or (os.environ.get(ORG_ENV) or "").strip()
        or DEFAULT_OWNER
    )


def _repo() -> str:
    return (os.environ.get(REPO_ENV) or "").strip() or DEFAULT_REPO


def _gh(*args: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(["gh", *args], text=True, capture_output=True)


def repo_detail(owner: str, name: str) -> dict | None:
    proc = _gh(
        "api",
        f"repos/{owner}/{name}",
        "--jq",
        "{name:.name, full_name:.full_name, default_branch:.default_branch, "
        "visibility:.visibility, description:.description}",
    )
    if proc.returncode != 0:
        return None
    return json.loads(proc.stdout)


def create_repo(owner: str, name: str) -> bool:
    proc = _gh(
        "repo",
        "create",
        f"{owner}/{name}",
        "--public",
        "--description",
        EXPECTED_DESCRIPTION,
    )
    if proc.returncode != 0:
        print(f"ERROR: gh repo create {owner}/{name} failed (exit {proc.returncode})")
        print(proc.stderr.strip())
        return False
    print(f"created  {owner}/{name} (public)")
    return True


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument(
        "--create-missing",
        action="store_true",
        help="idempotently create the public lucalamalfa91/raffa repo if absent",
    )
    args = ap.parse_args()

    owner = _owner()
    name = _repo()
    print(f"[verify_github_repos] owner={owner} repo={name}")

    detail = repo_detail(owner, name)
    if detail is None:
        if args.create_missing:
            if not create_repo(owner, name):
                print("[verify_github_repos] FAIL: see above")
                return 1
            detail = repo_detail(owner, name)
            if detail is None:
                print(f"ERROR    {owner}/{name}: created but could not re-read")
                return 1
        else:
            print(f"MISSING  {owner}/{name}")
            print("[verify_github_repos] FAIL: see above")
            return 1

    branch_ok = detail.get("default_branch") == "main"
    vis_ok = detail.get("visibility") == EXPECTED_VISIBILITY
    desc = (detail.get("description") or "").strip()
    desc_ok = desc.lower() == EXPECTED_DESCRIPTION.lower()
    ok = branch_ok and vis_ok and desc_ok
    status = "OK" if ok else "FAIL"
    print(
        f"{status:8} {detail.get('full_name')}: "
        f"default_branch={detail.get('default_branch')} "
        f"visibility={detail.get('visibility')} "
        f"description={desc!r}"
    )
    if not branch_ok:
        print("         expected default_branch=main")
    if not vis_ok:
        print(f"         expected visibility={EXPECTED_VISIBILITY} (do not make the repo private)")
    if not desc_ok:
        print(f"         expected description={EXPECTED_DESCRIPTION!r}")

    if ok:
        print(
            f"[verify_github_repos] PASS: {owner}/{name} is public, "
            f"default_branch=main, description={EXPECTED_DESCRIPTION!r}"
        )
        return 0
    print("[verify_github_repos] FAIL: see above")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
