CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903084706_Initial') THEN
    CREATE TABLE workspace (
        id uuid NOT NULL,
        name character varying(200) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_workspace PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903084706_Initial') THEN
    CREATE TABLE workspace_role (
        id uuid NOT NULL,
        name character varying(30) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_workspace_role PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903084706_Initial') THEN
    CREATE TABLE workspace_user (
        id uuid NOT NULL,
        email character varying(320) NOT NULL,
        display_name character varying(200),
        external_subject_id character varying(200),
        created_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_workspace_user PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903084706_Initial') THEN
    CREATE TABLE workspace_membership (
        id uuid NOT NULL,
        workspace_user_id uuid NOT NULL,
        workspace_role_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_workspace_membership PRIMARY KEY (id),
        CONSTRAINT fk_workspace_membership_workspace_role_workspace_role_id FOREIGN KEY (workspace_role_id) REFERENCES workspace_role (id) ON DELETE RESTRICT,
        CONSTRAINT fk_workspace_membership_workspace_user_workspace_user_id FOREIGN KEY (workspace_user_id) REFERENCES workspace_user (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903084706_Initial') THEN
    CREATE UNIQUE INDEX ix_workspace_tenant_id ON workspace (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903084706_Initial') THEN
    CREATE INDEX ix_workspace_membership_tenant_id ON workspace_membership (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903084706_Initial') THEN
    CREATE INDEX ix_workspace_membership_workspace_role_id ON workspace_membership (workspace_role_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903084706_Initial') THEN
    CREATE UNIQUE INDEX ix_workspace_membership_workspace_user_id_workspace_role_id ON workspace_membership (workspace_user_id, workspace_role_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903084706_Initial') THEN
    CREATE INDEX ix_workspace_role_tenant_id ON workspace_role (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903084706_Initial') THEN
    CREATE UNIQUE INDEX ix_workspace_role_tenant_id_name ON workspace_role (tenant_id, name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903084706_Initial') THEN
    CREATE INDEX ix_workspace_user_tenant_id ON workspace_user (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903084706_Initial') THEN
    CREATE UNIQUE INDEX ix_workspace_user_tenant_id_email ON workspace_user (tenant_id, email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903084706_Initial') THEN
    CREATE UNIQUE INDEX ix_workspace_user_tenant_id_external_subject_id ON workspace_user (tenant_id, external_subject_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903084706_Initial') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260903084706_Initial', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903084728_AddTenantRowLevelSecurity') THEN
    ALTER TABLE "workspace" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "workspace" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "workspace"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903084728_AddTenantRowLevelSecurity') THEN
    ALTER TABLE "workspace_user" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "workspace_user" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "workspace_user"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903084728_AddTenantRowLevelSecurity') THEN
    ALTER TABLE "workspace_role" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "workspace_role" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "workspace_role"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903084728_AddTenantRowLevelSecurity') THEN
    ALTER TABLE "workspace_membership" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "workspace_membership" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "workspace_membership"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903084728_AddTenantRowLevelSecurity') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260903084728_AddTenantRowLevelSecurity', '10.0.4');
    END IF;
END $EF$;
COMMIT;

