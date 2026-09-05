CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904223112_Initial') THEN
    CREATE TABLE renewal_action (
        id uuid NOT NULL,
        contract_id uuid NOT NULL,
        owner character varying(200) NOT NULL,
        status character varying(20) NOT NULL,
        action text NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_renewal_action PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904223112_Initial') THEN
    CREATE INDEX ix_renewal_action_tenant_id ON renewal_action (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904223112_Initial') THEN
    CREATE UNIQUE INDEX ix_renewal_action_tenant_id_contract_id ON renewal_action (tenant_id, contract_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904223112_Initial') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260904223112_Initial', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904223135_AddTenantRowLevelSecurity') THEN
    ALTER TABLE "renewal_action" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "renewal_action" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "renewal_action"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904223135_AddTenantRowLevelSecurity') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260904223135_AddTenantRowLevelSecurity', '10.0.4');
    END IF;
END $EF$;
COMMIT;

