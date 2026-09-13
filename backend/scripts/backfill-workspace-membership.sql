-- Backfill one Admin `workspace_membership` for an existing workspace whose
-- creator was never recorded (task E14/F05/US01/T01, ADR-025 SS2.3, ADR-026
-- implication 9 -- "claim this workspace" is refused as a tenant-takeover
-- primitive, so a human supplies (workspace id, admin email) pairs instead).
-- Invoked once per pair by .github/workflows/backfill-workspace-membership.yml:
--   psql -v ON_ERROR_STOP=1 -v workspace_id="$WORKSPACE_ID" -v admin_email="$EMAIL" \
--        -f backend/scripts/backfill-workspace-membership.sql
--
-- Both :workspace_id and :admin_email arrive as psql variables (-v), never
-- spliced into this text, so a quote character in either can never break out
-- of a statement. Originally an inline heredoc in the workflow step itself;
-- moved to this checked-in file (2026-09-13) because a `psql`-only
-- `<<SQL ... SQL` heredoc body cannot be indented to satisfy the YAML block
-- scalar it lived in without also breaking the heredoc's own closing
-- delimiter match -- the same file-not-inline-script shape
-- demo-fixture-seed.sql (task E10/F01/US01/T01) already established for
-- seed-demo-fixture.yml.
SET app.tenant_id = :'workspace_id';

BEGIN;

-- Case-insensitive existence check: an app-created workspace_user is never
-- lower-cased, so a prior invite/sign-in under different casing is reused
-- rather than duplicated. A genuinely new person is inserted with the
-- (already lower-cased) supplied address.
INSERT INTO workspace_user (id, email, display_name, external_subject_id, created_at, tenant_id)
SELECT gen_random_uuid(), :'admin_email', NULL, NULL, now(), :'workspace_id'::uuid
WHERE NOT EXISTS (
    SELECT 1 FROM workspace_user
    WHERE tenant_id = :'workspace_id'::uuid
      AND lower(email) = lower(:'admin_email')
)
ON CONFLICT (tenant_id, email) DO NOTHING;

INSERT INTO workspace_membership (id, workspace_user_id, workspace_role_id, created_at, tenant_id)
SELECT gen_random_uuid(), wu.id, wr.id, now(), :'workspace_id'::uuid
FROM workspace_user wu
JOIN workspace_role wr ON wr.tenant_id = wu.tenant_id AND wr.name = 'Admin'
WHERE wu.tenant_id = :'workspace_id'::uuid
  AND lower(wu.email) = lower(:'admin_email')
ON CONFLICT (workspace_user_id, workspace_role_id) DO NOTHING;

COMMIT;
