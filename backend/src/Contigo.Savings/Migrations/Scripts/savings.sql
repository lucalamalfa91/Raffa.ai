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

