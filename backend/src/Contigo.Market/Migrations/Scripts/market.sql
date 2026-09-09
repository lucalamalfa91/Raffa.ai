-- Contigo.Market — shared, read-only market-intelligence index (ADR-024 "three sources, one
-- rule"; ADR-011 epic-13 amendment). `market_record`/`market_embedding` carry no `tenant_id` and
-- no RLS policy by design — every tenant may read them, only the ingestion job
-- (Contigo.Market.Ingestion.MarketIngestionService, run via `dotnet run --project
-- backend/src/Contigo.Worker -- ingest-market`) ever writes them. Generated with `dotnet ef
-- migrations script --idempotent` from backend/src/Contigo.Market (task E13/F02/US01/T02);
-- applied by `.github/workflows/backend.yml`'s ADR-021 schema-apply step, in that workflow's
-- fixed module order.
--
-- Grant model (task objective: "grants read to the application role and restricts writes to the
-- ingestion role when the CI role model allows it"): Terraform (`infra/modules/postgres/main.tf`)
-- provisions exactly one Postgres role today — `administrator_login` — used identically by CI to
-- apply this very script (ADR-021 "Decision outcome": "Scripts run as the Flexible Server
-- administrator") and by the API/Worker hosts for every `ConnectionStrings:*` secret
-- (`postgres-connection`, the same value for every module; see `infra/README.md` "Known gaps").
-- There is no separate low-privilege application role and no separate ingestion role in that CI
-- role model yet, so this script cannot literally `GRANT` to either without failing CI's `psql -v
-- ON_ERROR_STOP=1 -f` with "role ... does not exist" the moment it runs against a fresh
-- `contigo_dev`/`contigo_demo`. The two conditional blocks at the end of this file record the
-- intended grant — read-only for `contigo_app`, read/write for `contigo_market_ingest` — as a
-- no-op today (`pg_roles` existence check) that self-activates, with no further edit to this
-- file, the day a later infra task provisions those roles (names are this file's own choice, not
-- an ADR-locked identifier — only the *mechanism*, a conditional grant keyed on `pg_roles`, is
-- fixed here; same "mechanism fixed, names council/infra-owned" posture
-- `reports/open-questions.md` OQ-DM-005 already uses for the demo tag/environment names).
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260909011226_Initial') THEN
    CREATE EXTENSION IF NOT EXISTS vector;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260909011226_Initial') THEN
    CREATE TABLE market_record (
        record_id character varying(200) NOT NULL,
        feed_version character varying(100) NOT NULL,
        provider character varying(200) NOT NULL,
        payload_json jsonb NOT NULL,
        provenance_label character varying(500) NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_market_record PRIMARY KEY (record_id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260909011226_Initial') THEN
    CREATE TABLE market_embedding (
        id uuid NOT NULL,
        record_id character varying(200) NOT NULL,
        chunk_index integer NOT NULL,
        chunk_text text NOT NULL,
        vector vector(1536) NOT NULL,
        model character varying(200) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_market_embedding PRIMARY KEY (id),
        CONSTRAINT fk_market_embedding_market_record_record_id FOREIGN KEY (record_id) REFERENCES market_record (record_id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260909011226_Initial') THEN
    CREATE INDEX ix_market_embedding_record_id ON market_embedding (record_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260909011226_Initial') THEN
    CREATE INDEX ix_market_record_provider ON market_record (provider);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260909011226_Initial') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260909011226_Initial', '10.0.4');
    END IF;
END $EF$;
COMMIT;

-- Grant model (see this file's own header comment). Not part of the "20260909011226_Initial"
-- EF migration and not gated on __EFMigrationsHistory: re-running these two blocks against an
-- already-granted role is a harmless re-GRANT (Postgres allows granting a privilege a role
-- already holds), so this stays safe to `psql -f` on every CI apply, not just the first.
DO $EF$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'contigo_app') THEN
        GRANT USAGE ON SCHEMA public TO contigo_app;
        GRANT SELECT ON market_record, market_embedding TO contigo_app;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'contigo_market_ingest') THEN
        GRANT USAGE ON SCHEMA public TO contigo_market_ingest;
        GRANT SELECT, INSERT, UPDATE, DELETE ON market_record, market_embedding TO contigo_market_ingest;
    END IF;
END $EF$;

