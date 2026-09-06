#!/usr/bin/env python3
"""Prove the six ADR-021 idempotent module scripts actually applied to this
environment's Postgres (task E09/F02/US01/T02, schema-applied).

Each checked-in `Migrations/Scripts/<module>.sql` (task E09/F01/US01/T01,
`dotnet ef migrations script --idempotent`) ends every migration's own
transaction with the same EF-generated trailer:

    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('<migration_id>', '<version>');

guarded by the identical `IF NOT EXISTS(...)` check the rest of that
migration's DDL uses. That row is the authoritative "did this migration
apply" ledger EF itself relies on; this script does not invent a second
one -- it is exactly the "(or `__EFMigrationsHistory` rows)" check the
parent task's own Definition of Done names as an accepted proof of "prove
`contigo_<env>` has the app tables (or fail the job)".

This script reads the *expected* migration ids straight out of the same
checked-in files `.github/workflows/backend.yml`'s "Apply schema
(ADR-021)" step just ran through `psql`, and compares them against the
*actual* ids its sibling "Verify schema applied (ADR-021)" step queried
live from `contigo_<env>`. It never opens a network connection or a
database credential itself -- the workflow runs the live
`psql -Atqc "SELECT migration_id FROM ..."` query and pipes the result in,
the same "Azure/DB lookups stay in the workflow" split
`scripts/pg_connection_string_env.py` uses for the connection string.

Usage:
    psql -Atqc 'SELECT migration_id FROM "__EFMigrationsHistory";' \\
      | python3 scripts/schema_apply_verify.py \\
          --script backend/src/Contigo.Identity.Workspace/Migrations/Scripts/identity-workspace.sql \\
          --script backend/src/Contigo.Documents.Contracts/Migrations/Scripts/documents-contracts.sql \\
          --script backend/src/Contigo.Audit/Migrations/Scripts/audit.sql \\
          --script backend/src/Contigo.Renewals/Migrations/Scripts/renewals.sql \\
          --script backend/src/Contigo.Savings/Migrations/Scripts/savings.sql \\
          --script backend/src/Contigo.Quotes/Migrations/Scripts/quotes.sql \\
          --applied-ids-file -

Exit 0 with one "[PASS]"/"[FAIL]" line per --script plus a summary line if
every migration_id every script declares is present in the applied set.
Non-zero otherwise, naming exactly which script/migration_id is missing.

    python3 scripts/schema_apply_verify.py --self-test
"""

from __future__ import annotations

import argparse
import re
import sys
import tempfile
from pathlib import Path

INSERT_RE = re.compile(
    r'INSERT INTO\s+"__EFMigrationsHistory"[^;]*?VALUES\s*\(\s*\'([^\']+)\'',
    re.IGNORECASE | re.DOTALL,
)


class ScriptReadError(ValueError):
    """A named --script path does not exist or declares zero migrations."""


def extract_migration_ids(sql_text: str) -> list:
    """Ordered, de-duplicated migration_id values this idempotent script's
    own `INSERT INTO "__EFMigrationsHistory"` trailers declare (one per
    migration the file contains, even though the file also repeats the
    same id in every `IF NOT EXISTS(...)` guard)."""
    seen: dict = {}
    for match in INSERT_RE.finditer(sql_text):
        seen.setdefault(match.group(1), None)
    return list(seen)


def expected_migrations_by_script(script_paths) -> dict:
    expected: dict = {}
    for path_str in script_paths:
        path = Path(path_str)
        if not path.is_file():
            raise ScriptReadError(f"{path_str} does not exist")
        ids = extract_migration_ids(path.read_text(encoding="utf-8"))
        if not ids:
            raise ScriptReadError(
                f'{path_str} declares no INSERT INTO "__EFMigrationsHistory" trailer -- '
                "not a `dotnet ef migrations script --idempotent` output?"
            )
        expected[path_str] = ids
    return expected


def missing_by_script(expected: dict, applied: set) -> dict:
    missing = {}
    for path_str, ids in expected.items():
        gap = [migration_id for migration_id in ids if migration_id not in applied]
        if gap:
            missing[path_str] = gap
    return missing


def _parse_applied_ids(text: str) -> set:
    return {line.strip() for line in text.splitlines() if line.strip()}


def _parse_args(argv):
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    parser.add_argument(
        "--script",
        dest="scripts",
        action="append",
        required=True,
        help="Repeat once per checked-in Migrations/Scripts/<module>.sql, in ADR-021 order.",
    )
    parser.add_argument(
        "--applied-ids-file",
        required=True,
        help="Path to a file of one migration_id per line (the live psql query result), or '-' for stdin.",
    )
    return parser.parse_args(argv)


def _read_applied_ids(applied_ids_file: str) -> str:
    if applied_ids_file == "-":
        return sys.stdin.read()
    applied_path = Path(applied_ids_file)
    if not applied_path.is_file():
        raise ScriptReadError(f"{applied_ids_file} does not exist")
    return applied_path.read_text(encoding="utf-8")


def run(script_paths, applied_ids_text: str, out=sys.stdout, err=sys.stderr) -> int:
    try:
        expected = expected_migrations_by_script(script_paths)
    except ScriptReadError as exc:
        print(f"[schema_apply_verify] FAIL: {exc}", file=err)
        return 1

    applied = _parse_applied_ids(applied_ids_text)
    if not applied:
        print(
            "[schema_apply_verify] FAIL: contigo_<env>.__EFMigrationsHistory returned zero rows -- "
            "the apply step did not run, or ran against the wrong database",
            file=err,
        )
        return 1

    gaps = missing_by_script(expected, applied)
    ok = True
    for path_str, ids in expected.items():
        if path_str in gaps:
            ok = False
            print(f"[FAIL] {path_str}: missing migration_id(s) {', '.join(gaps[path_str])} in __EFMigrationsHistory", file=out)
        else:
            print(f"[PASS] {path_str}: {len(ids)} migration_id(s) present in __EFMigrationsHistory", file=out)

    if ok:
        total = sum(len(v) for v in expected.values())
        print(
            f"[schema_apply_verify] PASS: all {total} migration_id(s) across {len(expected)} "
            "module script(s) are present in __EFMigrationsHistory (ADR-021)",
            file=out,
        )
        return 0
    print("[schema_apply_verify] FAIL: see above", file=err)
    return 1


def main(argv=None) -> int:
    if argv == ["--self-test"] or (argv is None and sys.argv[1:] == ["--self-test"]):
        return _run_self_test()

    args = _parse_args(argv)
    try:
        applied_text = _read_applied_ids(args.applied_ids_file)
    except ScriptReadError as exc:
        print(f"[schema_apply_verify] FAIL: {exc}", file=sys.stderr)
        return 1
    return run(args.scripts, applied_text)


def _run_self_test() -> int:
    initial_sql = """\
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (migration_id character varying(150) NOT NULL);
START TRANSACTION;
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260101000000_Initial') THEN
    CREATE TABLE widget (id uuid NOT NULL);
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260101000000_Initial') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260101000000_Initial', '10.0.4');
    END IF;
END $EF$;
COMMIT;
START TRANSACTION;
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260102000000_AddThing') THEN
    ALTER TABLE widget ADD COLUMN name text;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260102000000_AddThing') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260102000000_AddThing', '10.0.4');
    END IF;
END $EF$;
COMMIT;
"""
    ids = extract_migration_ids(initial_sql)
    assert ids == ["20260101000000_Initial", "20260102000000_AddThing"], ids
    print("[PASS] extract_migration_ids: two migrations, de-duplicated, in file order")

    assert extract_migration_ids("-- no EF trailer here\nSELECT 1;\n") == []
    print("[PASS] extract_migration_ids: a file with no INSERT trailer yields an empty list")

    with tempfile.TemporaryDirectory() as tmp:
        tmp_path = Path(tmp)
        module_a = tmp_path / "a.sql"
        module_a.write_text(initial_sql, encoding="utf-8")
        module_b = tmp_path / "b.sql"
        module_b.write_text(
            "INSERT INTO \"__EFMigrationsHistory\" (migration_id, product_version) "
            "VALUES ('20260103000000_B', '10.0.4');\n",
            encoding="utf-8",
        )
        empty_module = tmp_path / "empty.sql"
        empty_module.write_text("SELECT 1;\n", encoding="utf-8")

        expected = expected_migrations_by_script([str(module_a), str(module_b)])
        assert expected == {
            str(module_a): ["20260101000000_Initial", "20260102000000_AddThing"],
            str(module_b): ["20260103000000_B"],
        }, expected
        print("[PASS] expected_migrations_by_script: reads real files, one entry per --script")

        try:
            expected_migrations_by_script([str(empty_module)])
        except ScriptReadError:
            pass
        else:
            raise AssertionError("expected ScriptReadError for a script with no EF trailer")
        try:
            expected_migrations_by_script([str(tmp_path / "missing.sql")])
        except ScriptReadError:
            pass
        else:
            raise AssertionError("expected ScriptReadError for a missing script path")
        print("[PASS] expected_migrations_by_script: raises ScriptReadError for a missing/trailer-less script")

        # Full run(): everything applied -> PASS.
        applied_all = "20260101000000_Initial\n20260102000000_AddThing\n20260103000000_B\n"
        rc = run([str(module_a), str(module_b)], applied_all)
        assert rc == 0, rc
        print("[PASS] run(): exit 0 when every expected migration_id is in the applied set")

        # One module's second migration never landed -> FAIL, names the gap.
        applied_partial = "20260101000000_Initial\n20260103000000_B\n"
        rc = run([str(module_a), str(module_b)], applied_partial)
        assert rc == 1, rc
        print("[PASS] run(): exit 1 and names the missing migration_id when one did not apply")

        # Empty applied set (query ran against the wrong DB, or apply never ran) -> FAIL.
        rc = run([str(module_a)], "")
        assert rc == 1, rc
        print("[PASS] run(): exit 1 when the applied-ids set is empty")

    print("[schema_apply_verify] PASS: self-test")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
