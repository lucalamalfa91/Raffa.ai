#!/usr/bin/env python3
"""Convert the `postgres-connection` Key Vault secret (Npgsql keyword=value
format) into libpq/psql environment-variable `export` lines (task
E09/F02/US01/T02, schema-applied; ADR-021).

`infra/modules/postgres/main.tf` writes that secret as:

    Host=<fqdn>;Database=<db>;Username=<user>;Password=<random>;Ssl Mode=Require

-- the .NET Npgsql driver's own connection-string dialect (`Database`/
`Username`), not a libpq/psql conninfo string (`dbname`/`user`). `psql`
cannot consume it directly. This script does that one, pure, unit-testable
translation; `.github/workflows/backend.yml` owns every Azure/network-
touching call (fetching the secret from Key Vault, masking it, `eval`-ing
this script's output, then actually invoking `psql`) -- the same "Azure
lookups stay in the workflow, the script only shapes/validates" split
`scripts/write_web_runtime_config.py` already uses for the web deploy job.

The administrator password's charset is fixed by
`random_password.administrator` (`infra/modules/postgres/main.tf`):
alphanumeric plus `!@#%^*-_=+`, with `;` deliberately excluded "so the
password cannot split the connection string". That is exactly why this
parser can safely split on `;` first and then on the *first* `=` per
segment -- a `=` can legitimately be part of the password, a `;` never can.

Usage (never pass the secret as an argv -- it would leak into `ps`):
    printf '%s' "$RAW_CONNECTION_STRING" | python3 scripts/pg_connection_string_env.py
    # stdout is shell `export KEY=VALUE` lines; the caller does:
    eval "$(printf '%s' "$RAW_CONNECTION_STRING" | python3 scripts/pg_connection_string_env.py)"

Exit 0 with the export lines on stdout. Exit 2 with nothing on stdout and a
message on stderr if a required field is missing or the input has no
key=value segments at all -- `eval ""` is a safe no-op, so the workflow's
own `set -euo pipefail` plus this script's explicit non-zero exit is what
turns a malformed secret into a failed step instead of `psql` silently
running against an empty environment.

    python3 scripts/pg_connection_string_env.py --self-test
"""

from __future__ import annotations

import shlex
import sys

REQUIRED_FIELDS = ("host", "database", "username", "password")
DEFAULT_PORT = "5432"
DEFAULT_SSL_MODE = "require"


class ConnectionStringError(ValueError):
    """The input is not a usable Npgsql keyword=value connection string."""


def parse_npgsql_connection_string(value: str) -> dict:
    """Split `Key=Value;Key2=Value2` on ';' first, then each segment on the
    FIRST '=' only -- see module docstring for why the split order matters.
    Returns lowercase-keyed fields, e.g. {"host": ..., "ssl mode": ...}."""
    fields: dict = {}
    for segment in value.split(";"):
        segment = segment.strip()
        if not segment:
            continue
        if "=" not in segment:
            raise ConnectionStringError(f"segment {segment!r} is not key=value")
        key, _, raw_value = segment.partition("=")
        fields[key.strip().lower()] = raw_value
    if not fields:
        raise ConnectionStringError("connection string has no key=value segments")
    return fields


def to_psql_env(fields: dict) -> dict:
    """Map Npgsql field names onto the libpq environment variables `psql`
    reads automatically -- the call site needs no extra flags/URI."""
    missing = [name for name in REQUIRED_FIELDS if not fields.get(name)]
    if missing:
        raise ConnectionStringError(
            f"connection string missing required field(s): {', '.join(missing)}"
        )
    ssl_mode = fields.get("ssl mode", DEFAULT_SSL_MODE).strip().lower()
    return {
        "PGHOST": fields["host"],
        "PGPORT": fields.get("port", DEFAULT_PORT),
        "PGDATABASE": fields["database"],
        "PGUSER": fields["username"],
        "PGPASSWORD": fields["password"],
        "PGSSLMODE": ssl_mode,
    }


def format_exports(env: dict) -> str:
    return "\n".join(f"export {key}={shlex.quote(value)}" for key, value in env.items())


def _run_self_test() -> int:
    # Short fixture password (contains '=' -- the one tricky case -- but
    # deliberately under 8 chars so it can never look like a real secret to
    # scripts/repo_secret_scan.py's `Password=<8+ chars>` heuristic).
    sample = (
        "Host=psql-raffa-dev.postgres.database.azure.com;"
        "Database=raffa_dev;Username=raffaadmin;Password=a=b!;"
        "Ssl Mode=Require"
    )
    fields = parse_npgsql_connection_string(sample)
    assert fields["host"] == "psql-raffa-dev.postgres.database.azure.com", fields
    assert fields["database"] == "raffa_dev", fields
    assert fields["username"] == "raffaadmin", fields
    # Only the FIRST '=' in the segment is the key/value separator, so '='
    # inside the password itself must survive intact.
    assert fields["password"] == "a=b!", fields
    assert fields["ssl mode"] == "Require", fields
    print("[PASS] parse_npgsql_connection_string: '=' inside the password survives; ';' still splits fields")

    env = to_psql_env(fields)
    assert env == {
        "PGHOST": "psql-raffa-dev.postgres.database.azure.com",
        "PGPORT": "5432",
        "PGDATABASE": "raffa_dev",
        "PGUSER": "raffaadmin",
        "PGPASSWORD": "a=b!",
        "PGSSLMODE": "require",
    }, env
    print("[PASS] to_psql_env: Host/Database/Username/Password/Ssl Mode -> PG*; PGPORT defaults to 5432")

    exports = format_exports(env)
    assert "export PGHOST=psql-raffa-dev.postgres.database.azure.com" in exports
    assert f"export PGPASSWORD={shlex.quote(env['PGPASSWORD'])}" in exports
    print("[PASS] format_exports: every value is shell-quoted (shlex.quote)")

    for missing_field in REQUIRED_FIELDS:
        broken = {k: v for k, v in fields.items() if k != missing_field}
        try:
            to_psql_env(broken)
        except ConnectionStringError:
            pass
        else:
            raise AssertionError(f"expected ConnectionStringError when {missing_field!r} is missing")
    print("[PASS] to_psql_env: raises ConnectionStringError when a required field is missing")

    try:
        parse_npgsql_connection_string("not-a-key-value-segment")
    except ConnectionStringError:
        pass
    else:
        raise AssertionError("expected ConnectionStringError for a segment with no '='")
    try:
        parse_npgsql_connection_string("   ")
    except ConnectionStringError:
        pass
    else:
        raise AssertionError("expected ConnectionStringError for an all-blank input")
    print("[PASS] parse_npgsql_connection_string: rejects a segment with no '=' and an all-blank input")

    print("[pg_connection_string_env] PASS: self-test")
    return 0


def main(argv=None) -> int:
    argv = sys.argv[1:] if argv is None else argv
    if argv == ["--self-test"]:
        return _run_self_test()
    if argv:
        print(
            f"error: unrecognized argument(s): {argv!r} (pass the connection string on stdin, not argv)",
            file=sys.stderr,
        )
        return 2

    raw = sys.stdin.read().rstrip("\r\n")
    if not raw:
        print("error: empty input on stdin", file=sys.stderr)
        return 2
    try:
        fields = parse_npgsql_connection_string(raw)
        env = to_psql_env(fields)
    except ConnectionStringError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2
    print(format_exports(env))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
