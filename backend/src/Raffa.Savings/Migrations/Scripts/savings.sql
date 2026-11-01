CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905080356_Initial') THEN
    CREATE TABLE savings_opportunity (
        id uuid NOT NULL,
        supplier_id uuid,
        contract_id uuid,
        type character varying(100) NOT NULL,
        current_spend numeric(18,2) NOT NULL,
        currency character varying(3) NOT NULL,
        estimated_savings_low numeric(18,2) NOT NULL,
        estimated_savings_high numeric(18,2) NOT NULL,
        confidence double precision NOT NULL,
        status character varying(20) NOT NULL,
        owner character varying(200),
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_savings_opportunity PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905080356_Initial') THEN
    CREATE INDEX ix_savings_opportunity_tenant_id ON savings_opportunity (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905080356_Initial') THEN
    CREATE INDEX ix_savings_opportunity_tenant_id_contract_id ON savings_opportunity (tenant_id, contract_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905080356_Initial') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260905080356_Initial', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905080413_AddTenantRowLevelSecurity') THEN
    ALTER TABLE "savings_opportunity" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "savings_opportunity" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "savings_opportunity"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905080413_AddTenantRowLevelSecurity') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260905080413_AddTenantRowLevelSecurity', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905084747_AddRealizedSavings') THEN
    CREATE TABLE realized_savings (
        id uuid NOT NULL,
        savings_opportunity_id uuid NOT NULL,
        amount numeric(18,2) NOT NULL,
        currency character varying(3) NOT NULL,
        realized_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_realized_savings PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905084747_AddRealizedSavings') THEN
    CREATE INDEX ix_realized_savings_tenant_id ON realized_savings (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905084747_AddRealizedSavings') THEN
    CREATE INDEX ix_realized_savings_tenant_id_savings_opportunity_id ON realized_savings (tenant_id, savings_opportunity_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905084747_AddRealizedSavings') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260905084747_AddRealizedSavings', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905084804_AddRealizedSavingsRowLevelSecurity') THEN
    ALTER TABLE "realized_savings" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "realized_savings" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "realized_savings"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905084804_AddRealizedSavingsRowLevelSecurity') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260905084804_AddRealizedSavingsRowLevelSecurity', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260921212938_AddOpportunityKey') THEN
    ALTER TABLE savings_opportunity ADD opportunity_key character varying(120);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260921212938_AddOpportunityKey') THEN
    CREATE UNIQUE INDEX ix_savings_opportunity_tenant_id_contract_id_opportunity_key ON savings_opportunity (tenant_id, contract_id, opportunity_key) WHERE opportunity_key IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260921212938_AddOpportunityKey') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260921212938_AddOpportunityKey', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101115003_AddVerifiableSavings') THEN
    CREATE TABLE case_objective (
        id uuid NOT NULL,
        case_id uuid NOT NULL,
        contract_id uuid NOT NULL,
        owner_name character varying(200),
        kind character varying(20),
        pct numeric(9,2),
        amount numeric(18,2),
        text character varying(1000),
        outcome character varying(20),
        updated_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_case_objective PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101115003_AddVerifiableSavings') THEN
    CREATE TABLE invoice (
        id uuid NOT NULL,
        supplier_id uuid,
        supplier_name character varying(200) NOT NULL,
        contract_id uuid,
        period date NOT NULL,
        amount numeric(18,2) NOT NULL,
        currency character varying(3) NOT NULL,
        document_id uuid,
        created_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_invoice PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101115003_AddVerifiableSavings') THEN
    CREATE TABLE saving_result (
        id uuid NOT NULL,
        contract_id uuid,
        case_id uuid,
        supplier_id uuid,
        supplier_name character varying(200) NOT NULL,
        contract_name character varying(300) NOT NULL,
        category character varying(100),
        kind character varying(20) NOT NULL,
        counts_as_negotiation boolean NOT NULL,
        amount_annual numeric(18,2) NOT NULL,
        currency character varying(3) NOT NULL,
        note text NOT NULL,
        baseline_desc text NOT NULL,
        baseline_value numeric(18,4),
        new_value numeric(18,4),
        quantity numeric(18,4),
        method text NOT NULL,
        period_from date,
        period_to date,
        period_desc text NOT NULL,
        evidence_document_id uuid,
        evidence_file_name character varying(300) NOT NULL,
        owner_name character varying(200),
        signed_on date NOT NULL,
        recorded_at timestamp with time zone NOT NULL,
        recorded_by character varying(200),
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_saving_result PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101115003_AddVerifiableSavings') THEN
    CREATE TABLE savings_goal (
        id uuid NOT NULL,
        user_id character varying(200) NOT NULL,
        source_text character varying(1000) NOT NULL,
        pct numeric(9,2),
        annual_amount numeric(18,2),
        scope_json jsonb NOT NULL,
        period character varying(40) NOT NULL,
        active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_savings_goal PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101115003_AddVerifiableSavings') THEN
    CREATE TABLE saving_realization (
        id uuid NOT NULL,
        saving_result_id uuid NOT NULL,
        invoice_id uuid NOT NULL,
        realized_amount numeric(18,2) NOT NULL,
        computed_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_saving_realization PRIMARY KEY (id),
        CONSTRAINT fk_saving_realization_invoice_invoice_id FOREIGN KEY (invoice_id) REFERENCES invoice (id) ON DELETE CASCADE,
        CONSTRAINT fk_saving_realization_saving_result_saving_result_id FOREIGN KEY (saving_result_id) REFERENCES saving_result (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101115003_AddVerifiableSavings') THEN
    CREATE INDEX ix_case_objective_tenant_id ON case_objective (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101115003_AddVerifiableSavings') THEN
    CREATE UNIQUE INDEX ix_case_objective_tenant_id_case_id ON case_objective (tenant_id, case_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101115003_AddVerifiableSavings') THEN
    CREATE INDEX ix_case_objective_tenant_id_contract_id ON case_objective (tenant_id, contract_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101115003_AddVerifiableSavings') THEN
    CREATE INDEX ix_invoice_tenant_id ON invoice (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101115003_AddVerifiableSavings') THEN
    CREATE INDEX ix_invoice_tenant_id_contract_id_period ON invoice (tenant_id, contract_id, period);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101115003_AddVerifiableSavings') THEN
    CREATE INDEX ix_saving_realization_invoice_id ON saving_realization (invoice_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101115003_AddVerifiableSavings') THEN
    CREATE UNIQUE INDEX ix_saving_realization_saving_result_id_invoice_id ON saving_realization (saving_result_id, invoice_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101115003_AddVerifiableSavings') THEN
    CREATE INDEX ix_saving_realization_tenant_id ON saving_realization (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101115003_AddVerifiableSavings') THEN
    CREATE INDEX ix_saving_result_tenant_id ON saving_result (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101115003_AddVerifiableSavings') THEN
    CREATE INDEX ix_saving_result_tenant_id_contract_id ON saving_result (tenant_id, contract_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101115003_AddVerifiableSavings') THEN
    CREATE INDEX ix_saving_result_tenant_id_signed_on ON saving_result (tenant_id, signed_on);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101115003_AddVerifiableSavings') THEN
    CREATE INDEX ix_savings_goal_tenant_id ON savings_goal (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101115003_AddVerifiableSavings') THEN
    CREATE UNIQUE INDEX ux_savings_goal_active_per_user ON savings_goal (tenant_id, user_id) WHERE active;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101115003_AddVerifiableSavings') THEN
    ALTER TABLE "saving_result" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "saving_result" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "saving_result"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101115003_AddVerifiableSavings') THEN
    ALTER TABLE "invoice" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "invoice" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "invoice"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101115003_AddVerifiableSavings') THEN
    ALTER TABLE "saving_realization" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "saving_realization" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "saving_realization"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101115003_AddVerifiableSavings') THEN
    ALTER TABLE "savings_goal" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "savings_goal" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "savings_goal"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101115003_AddVerifiableSavings') THEN
    ALTER TABLE "case_objective" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "case_objective" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "case_objective"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101115003_AddVerifiableSavings') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261101115003_AddVerifiableSavings', '10.0.4');
    END IF;
END $EF$;
COMMIT;

