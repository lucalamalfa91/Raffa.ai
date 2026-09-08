-- Demo fixture seed (task E10/F01/US01/T01, seed-demo-fixture).
--
-- ADR-001 (R3/R4 gated on the Benchmark Service fixture adapter, never a
-- paid API for the first `demo`), ADR-009 (RLS is the non-bypassable
-- backstop -- this script sets the same `app.tenant_id` session claim
-- `Contigo.SharedKernel.Tenancy.TenantRlsConnectionInterceptor` sets at
-- runtime, it never disables or bypasses a policy), ADR-021 (schema is
-- already applied by `.github/workflows/backend.yml`'s CI apply step --
-- this script only INSERTs, it never CREATEs/ALTERs a table and never
-- calls `Database.MigrateAsync()`), ADR-022 (Day-1 `demo` renders savings
-- opportunities from a seeded fixture, not a live extract).
--
-- `Contigo.Benchmark.Fixtures.FixtureBenchmarkAdapter` (the ADR-001
-- fixture adapter) is in-process and has nothing of its own to persist --
-- it is a hand-curated, in-memory catalog, not a database table. What the
-- Day-1 Savings UI actually reads is `GET /api/savings`, backed by the
-- `savings_opportunity` table (`Contigo.Savings.Application
-- .SavingsOpportunityService.ListAsync`). This script is that missing
-- persistence step: three `savings_opportunity` rows whose supplier/
-- product/currency and confidence score are traceable back to three real
-- `FixtureBenchmarkAdapter.Catalog` rows (AWS EC2, Zoom, Snowflake) and to
-- `Contigo.Savings.Application.SavingsProvenanceClassifier`'s own
-- documented High/Medium/Low examples for those exact fixtures -- not
-- arbitrary numbers -- plus one supporting `workspace` (the demo tenant)
-- and one supporting `contract` row so `contract_id`/`tenant_id` point at
-- real rows rather than dangling ids.
--
-- Precondition: e09's schema apply (ADR-021) has already run against this
-- database -- `workspace`/`contract`/`savings_opportunity` must already
-- exist. This script does not create them; `psql -v ON_ERROR_STOP=1` fails
-- loudly (relation does not exist) if it is run first.
--
-- Idempotent (AC-1 "repeatable"): every row below uses a fixed, well-known
-- id and `ON CONFLICT (id) DO NOTHING`, so running this script twice (or
-- against an environment that was already seeded) inserts nothing new and
-- never errors. Fixed ids, deliberately memorable and never colliding with
-- a real `EntityId.New()` (`Guid.NewGuid()`) row:
--
--   demo tenant / workspace : 00000000-0000-0000-0000-000000000001
--   demo contract           : 00000000-0000-0000-0000-000000000002
--   savings opportunity #1  : 00000000-0000-0000-0000-000000000011  (AWS EC2, High confidence)
--   savings opportunity #2  : 00000000-0000-0000-0000-000000000012  (Zoom, Medium confidence)
--   savings opportunity #3  : 00000000-0000-0000-0000-000000000013  (Snowflake, Low confidence)
--
-- An operator/tester exercises the seeded tenant with:
--   curl $API/api/savings -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000001"
--
-- AC-4 ("Seed does not disable RLS for the API identity"): this script
-- never touches a role, a GRANT, or a policy -- it only sets the same
-- per-session `app.tenant_id` claim the application itself sets, so every
-- INSERT below satisfies each table's own `tenant_isolation` policy
-- `WITH CHECK` clause the ordinary way, not by bypassing it.
SET app.tenant_id = '00000000-0000-0000-0000-000000000001';

BEGIN;

-- The demo tenant itself. `Contigo.Identity.Workspace.Domain.WorkspaceTenant`'s
-- own invariant: `id` always equals `tenant_id` (see that type's doc
-- comment -- "in V1 a workspace *is* a tenant").
INSERT INTO workspace (id, name, created_at, tenant_id)
VALUES (
    '00000000-0000-0000-0000-000000000001',
    'Contigo Demo',
    now(),
    '00000000-0000-0000-0000-000000000001'
)
ON CONFLICT (id) DO NOTHING;

-- One supporting contract row (story us-01-seed-demo-db AC-1's "plus
-- supporting contract row if required by FKs"). Not actually required --
-- `savings_opportunity.contract_id` is a cross-module reference by id
-- only, deliberately with no foreign key (ADR-002; see
-- `Contigo.Savings.Domain.SavingsOpportunity`'s own doc comment) -- but a
-- real row here means opportunity #1 below points at something genuine
-- instead of a dangling id.
INSERT INTO contract (
    id, supplier_id, parent_contract_id, type, status, currency,
    start_date, end_date, effective_date, cancellation_deadline,
    annual_spend, total_contract_value, auto_renewal, renewal_term_months,
    payment_terms, governing_law, created_at, tenant_id
)
VALUES (
    '00000000-0000-0000-0000-000000000002',
    NULL, NULL, 'OrderForm', 'Active', 'USD',
    '2026-01-01', '2026-12-31', '2026-01-01', '2026-11-01',
    189000.00, 189000.00, true, 12,
    'Net 30', 'Delaware, USA', now(), '00000000-0000-0000-0000-000000000001'
)
ON CONFLICT (id) DO NOTHING;

-- Opportunity #1 -- AWS EC2 Compute (US), m5.large, 12-month term.
-- FixtureBenchmarkAdapter.Catalog: P25=0.085 / P50=0.096 / P75=0.108 per
-- instance-hour, sample size 340 -- a full-dimension match on a full
-- sample, SavingsProvenanceClassifier's own documented example of
-- confidence 1.0 / High. Linked to the supporting contract above.
INSERT INTO savings_opportunity (
    id, supplier_id, contract_id, type, current_spend, currency,
    estimated_savings_low, estimated_savings_high, confidence, status,
    owner, created_at, updated_at, tenant_id
)
VALUES (
    '00000000-0000-0000-0000-000000000011',
    NULL, '00000000-0000-0000-0000-000000000002',
    'AWS EC2 Compute (US) - right-size against P25-P50 market rate',
    189000.00, 'USD', 16000.00, 31000.00, 1.0, 'Identified',
    NULL, now(), now(), '00000000-0000-0000-0000-000000000001'
)
ON CONFLICT (id) DO NOTHING;

-- Opportunity #2 -- Zoom Workplace Pro, 12-month term. Catalog: P25=140 /
-- P50=156 / P75=180 per seat/year, sample size 30 -- SavingsProvenanceClassifier's
-- own documented example of confidence 0.6 / Medium. No contract link
-- (SupplierId/ContractId are both nullable -- SavingsOpportunity's own doc
-- comment), already being pursued by Procurement.
INSERT INTO savings_opportunity (
    id, supplier_id, contract_id, type, current_spend, currency,
    estimated_savings_low, estimated_savings_high, confidence, status,
    owner, created_at, updated_at, tenant_id
)
VALUES (
    '00000000-0000-0000-0000-000000000012',
    NULL, NULL,
    'Zoom Workplace Pro - renegotiate at P50 seat rate',
    102000.00, 'USD', 12000.00, 22000.00, 0.6, 'InProgress',
    'Alex Procurement', now(), now(), '00000000-0000-0000-0000-000000000001'
)
ON CONFLICT (id) DO NOTHING;

-- Opportunity #3 -- Snowflake Standard Compute Credits, 12-month term.
-- Catalog: P25=2.10 / P50=2.35 / P75=2.60 per credit, sample size 18 --
-- SavingsProvenanceClassifier's own documented example of confidence 0.36
-- / Low: a real, honestly thin comparable, not a fabricated precise
-- number (ADR-001).
INSERT INTO savings_opportunity (
    id, supplier_id, contract_id, type, current_spend, currency,
    estimated_savings_low, estimated_savings_high, confidence, status,
    owner, created_at, updated_at, tenant_id
)
VALUES (
    '00000000-0000-0000-0000-000000000013',
    NULL, NULL,
    'Snowflake Standard Compute Credits - thin-sample market check',
    95000.00, 'USD', 8000.00, 21000.00, 0.36, 'Identified',
    NULL, now(), now(), '00000000-0000-0000-0000-000000000001'
)
ON CONFLICT (id) DO NOTHING;

COMMIT;
