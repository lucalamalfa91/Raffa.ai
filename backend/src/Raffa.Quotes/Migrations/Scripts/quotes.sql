CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905123116_Initial') THEN
    CREATE TABLE quote (
        id uuid NOT NULL,
        file_name character varying(500) NOT NULL,
        mime_type character varying(200) NOT NULL,
        storage_path character varying(1000) NOT NULL,
        checksum character varying(128) NOT NULL,
        processing_status character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_quote PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905123116_Initial') THEN
    CREATE TABLE quote_extraction_job (
        id uuid NOT NULL,
        quote_id uuid NOT NULL,
        status character varying(20) NOT NULL,
        model_id character varying(200),
        queued_at timestamp with time zone NOT NULL,
        started_at timestamp with time zone,
        completed_at timestamp with time zone,
        error_detail character varying(1000),
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_quote_extraction_job PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905123116_Initial') THEN
    CREATE TABLE quote_line (
        id uuid NOT NULL,
        quote_id uuid NOT NULL,
        sku character varying(100),
        edition character varying(100),
        description character varying(1000) NOT NULL,
        quantity numeric(18,4),
        unit character varying(50),
        unit_price numeric(18,2),
        list_price numeric(18,2),
        discount_percent numeric(9,4),
        term character varying(100),
        extended_price numeric(18,2),
        source_span character varying(2000),
        source_page integer,
        confidence double precision,
        created_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_quote_line PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905123116_Initial') THEN
    CREATE INDEX ix_quote_tenant_id ON quote (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905123116_Initial') THEN
    CREATE INDEX ix_quote_extraction_job_tenant_id ON quote_extraction_job (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905123116_Initial') THEN
    CREATE INDEX ix_quote_extraction_job_tenant_id_quote_id ON quote_extraction_job (tenant_id, quote_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905123116_Initial') THEN
    CREATE INDEX ix_quote_line_tenant_id ON quote_line (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905123116_Initial') THEN
    CREATE INDEX ix_quote_line_tenant_id_quote_id ON quote_line (tenant_id, quote_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905123116_Initial') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260905123116_Initial', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905123141_AddTenantRowLevelSecurity') THEN
    ALTER TABLE "quote" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "quote" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "quote"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905123141_AddTenantRowLevelSecurity') THEN
    ALTER TABLE "quote_extraction_job" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "quote_extraction_job" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "quote_extraction_job"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905123141_AddTenantRowLevelSecurity') THEN
    ALTER TABLE "quote_line" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "quote_line" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "quote_line"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905123141_AddTenantRowLevelSecurity') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260905123141_AddTenantRowLevelSecurity', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905132033_AddQuoteLineNormalizedUnitEconomics') THEN
    ALTER TABLE quote_line ADD normalized_annual_unit_price numeric(18,2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905132033_AddQuoteLineNormalizedUnitEconomics') THEN
    ALTER TABLE quote_line ADD normalized_term_months integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905132033_AddQuoteLineNormalizedUnitEconomics') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260905132033_AddQuoteLineNormalizedUnitEconomics', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905132255_AddSkuProductMapping') THEN
    ALTER TABLE quote_line ADD match_status character varying(20) NOT NULL DEFAULT 'Unmatched';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905132255_AddSkuProductMapping') THEN
    ALTER TABLE quote_line ADD normalized_edition character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905132255_AddSkuProductMapping') THEN
    ALTER TABLE quote_line ADD normalized_sku character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905132255_AddSkuProductMapping') THEN
    CREATE TABLE sku_product_mapping (
        id uuid NOT NULL,
        normalized_sku character varying(100) NOT NULL,
        normalized_edition character varying(100),
        canonical_sku character varying(100) NOT NULL,
        canonical_edition character varying(100),
        canonical_product_name character varying(500),
        created_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_sku_product_mapping PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905132255_AddSkuProductMapping') THEN
    CREATE INDEX ix_sku_product_mapping_tenant_id ON sku_product_mapping (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905132255_AddSkuProductMapping') THEN
    CREATE UNIQUE INDEX ix_sku_product_mapping_tenant_id_normalized_sku ON sku_product_mapping (tenant_id, normalized_sku);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905132255_AddSkuProductMapping') THEN
    ALTER TABLE "sku_product_mapping" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "sku_product_mapping" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "sku_product_mapping"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905132255_AddSkuProductMapping') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260905132255_AddSkuProductMapping', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905142208_AddQuoteBenchmarkMatchFields') THEN
    ALTER TABLE quote ADD currency character varying(3);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905142208_AddQuoteBenchmarkMatchFields') THEN
    ALTER TABLE quote ADD geography character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905142208_AddQuoteBenchmarkMatchFields') THEN
    ALTER TABLE quote ADD purchase_date date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905142208_AddQuoteBenchmarkMatchFields') THEN
    ALTER TABLE quote ADD supplier character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905142208_AddQuoteBenchmarkMatchFields') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260905142208_AddQuoteBenchmarkMatchFields', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905170108_AddNegotiationOutcome') THEN
    CREATE TABLE negotiation_outcome (
        id uuid NOT NULL,
        quote_id uuid NOT NULL,
        original_quote_total numeric(18,2) NOT NULL,
        target_price numeric(18,2),
        final_price numeric(18,2) NOT NULL,
        realized_saving numeric(18,2) NOT NULL,
        discount_percent numeric(9,4) NOT NULL,
        negotiation_duration_days integer NOT NULL,
        levers_used character varying(200) NOT NULL,
        captured_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_negotiation_outcome PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905170108_AddNegotiationOutcome') THEN
    CREATE INDEX ix_negotiation_outcome_tenant_id ON negotiation_outcome (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905170108_AddNegotiationOutcome') THEN
    CREATE INDEX ix_negotiation_outcome_tenant_id_quote_id ON negotiation_outcome (tenant_id, quote_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905170108_AddNegotiationOutcome') THEN
    ALTER TABLE "negotiation_outcome" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "negotiation_outcome" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "negotiation_outcome"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905170108_AddNegotiationOutcome') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260905170108_AddNegotiationOutcome', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905174847_AddNegotiationOutcomeSavingsOpportunityId') THEN
    ALTER TABLE negotiation_outcome ADD savings_opportunity_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905174847_AddNegotiationOutcomeSavingsOpportunityId') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260905174847_AddNegotiationOutcomeSavingsOpportunityId', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101114912_AddQuoteOfferDecision') THEN
    ALTER TABLE quote_line ADD existing_price_label character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101114912_AddQuoteOfferDecision') THEN
    ALTER TABLE quote_line ADD existing_unit_price numeric(18,4);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101114912_AddQuoteOfferDecision') THEN
    ALTER TABLE quote ADD case_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101114912_AddQuoteOfferDecision') THEN
    ALTER TABLE quote ADD checked_by character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101114912_AddQuoteOfferDecision') THEN
    ALTER TABLE quote ADD contract_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101114912_AddQuoteOfferDecision') THEN
    ALTER TABLE quote ADD decided_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101114912_AddQuoteOfferDecision') THEN
    ALTER TABLE quote ADD decided_by character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101114912_AddQuoteOfferDecision') THEN
    ALTER TABLE quote ADD extraction_confirmed_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101114912_AddQuoteOfferDecision') THEN
    ALTER TABLE quote ADD kind character varying(20);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101114912_AddQuoteOfferDecision') THEN
    ALTER TABLE quote ADD missing_json jsonb;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101114912_AddQuoteOfferDecision') THEN
    ALTER TABLE quote ADD quote_date date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101114912_AddQuoteOfferDecision') THEN
    ALTER TABLE quote ADD reference character varying(50);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101114912_AddQuoteOfferDecision') THEN
    ALTER TABLE quote ADD stated_terms jsonb;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101114912_AddQuoteOfferDecision') THEN
    ALTER TABLE quote ADD status character varying(20);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101114912_AddQuoteOfferDecision') THEN
    ALTER TABLE quote ADD target_json jsonb;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101114912_AddQuoteOfferDecision') THEN
    ALTER TABLE quote ADD term_label character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101114912_AddQuoteOfferDecision') THEN
    ALTER TABLE quote ADD valid_until date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101114912_AddQuoteOfferDecision') THEN
    CREATE INDEX ix_quote_tenant_id_status ON quote (tenant_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101114912_AddQuoteOfferDecision') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261101114912_AddQuoteOfferDecision', '10.0.4');
    END IF;
END $EF$;
COMMIT;

