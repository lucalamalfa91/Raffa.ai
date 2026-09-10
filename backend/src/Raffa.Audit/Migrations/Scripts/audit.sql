CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903091140_Initial') THEN
    CREATE TABLE audit_event (
        id uuid NOT NULL,
        actor character varying(200) NOT NULL,
        action character varying(100) NOT NULL,
        resource_type character varying(100) NOT NULL,
        resource_id character varying(200) NOT NULL,
        occurred_at timestamp with time zone NOT NULL,
        detail text,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_audit_event PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903091140_Initial') THEN
    CREATE INDEX ix_audit_event_tenant_id ON audit_event (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903091140_Initial') THEN
    CREATE INDEX ix_audit_event_tenant_id_occurred_at ON audit_event (tenant_id, occurred_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903091140_Initial') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260903091140_Initial', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903091156_AddTenantRowLevelSecurity') THEN
    ALTER TABLE "audit_event" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "audit_event" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "audit_event"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903091156_AddTenantRowLevelSecurity') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260903091156_AddTenantRowLevelSecurity', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903091204_AddAppendOnlyEnforcement') THEN
    CREATE FUNCTION audit_event_reject_mutation() RETURNS trigger
        LANGUAGE plpgsql
        AS $BODY$
        BEGIN
            RAISE EXCEPTION 'audit_event is append-only: % is not permitted', TG_OP
                USING ERRCODE = 'insufficient_privilege';
        END;
        $BODY$;

    CREATE TRIGGER audit_event_append_only
        BEFORE UPDATE OR DELETE ON "audit_event"
        FOR EACH ROW
        EXECUTE FUNCTION audit_event_reject_mutation();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903091204_AddAppendOnlyEnforcement') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260903091204_AddAppendOnlyEnforcement', '10.0.4');
    END IF;
END $EF$;
COMMIT;

