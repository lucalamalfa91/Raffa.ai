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

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260911084211_AddWorkspaceUserIdentitySelfReadPolicy') THEN
    CREATE POLICY identity_self ON "workspace_user"
        FOR SELECT
        USING (
            nullif(current_setting('app.identity_subject', true), '') IS NOT NULL
            AND (
                lower(email) = lower(nullif(current_setting('app.identity_subject', true), ''))
                OR external_subject_id = nullif(current_setting('app.identity_subject', true), '')
            )
        );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260911084211_AddWorkspaceUserIdentitySelfReadPolicy') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260911084211_AddWorkspaceUserIdentitySelfReadPolicy', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260911084315_AddWorkspaceProfileColumns') THEN
    ALTER TABLE workspace ADD country character varying(2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260911084315_AddWorkspaceProfileColumns') THEN
    ALTER TABLE workspace ADD currency character varying(3);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260911084315_AddWorkspaceProfileColumns') THEN
    ALTER TABLE workspace ADD industry character varying(120);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260911084315_AddWorkspaceProfileColumns') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260911084315_AddWorkspaceProfileColumns', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260911084444_AddWorkspaceInvitation') THEN
    CREATE TABLE workspace_invitation (
        id uuid NOT NULL,
        email character varying(320) NOT NULL,
        workspace_role_id uuid NOT NULL,
        token_hash character varying(128) NOT NULL,
        invited_by character varying(320) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        accepted_at timestamp with time zone,
        revoked_at timestamp with time zone,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_workspace_invitation PRIMARY KEY (id),
        CONSTRAINT fk_workspace_invitation_workspace_role_workspace_role_id FOREIGN KEY (workspace_role_id) REFERENCES workspace_role (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260911084444_AddWorkspaceInvitation') THEN
    CREATE INDEX ix_workspace_invitation_tenant_id ON workspace_invitation (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260911084444_AddWorkspaceInvitation') THEN
    CREATE UNIQUE INDEX ix_workspace_invitation_tenant_id_token_hash ON workspace_invitation (tenant_id, token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260911084444_AddWorkspaceInvitation') THEN
    CREATE INDEX ix_workspace_invitation_workspace_role_id ON workspace_invitation (workspace_role_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260911084444_AddWorkspaceInvitation') THEN
    CREATE UNIQUE INDEX ix_workspace_invitation_tenant_id_email ON "workspace_invitation" (tenant_id, lower(email))
        WHERE accepted_at IS NULL AND revoked_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260911084444_AddWorkspaceInvitation') THEN
    ALTER TABLE "workspace_invitation" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "workspace_invitation" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "workspace_invitation"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260911084444_AddWorkspaceInvitation') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260911084444_AddWorkspaceInvitation', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922170000_AddWorkspaceWebResearchEnabled') THEN
    ALTER TABLE workspace ADD web_research_enabled boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922170000_AddWorkspaceWebResearchEnabled') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260922170000_AddWorkspaceWebResearchEnabled', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261102100000_AddPlatformAdminTables') THEN
    CREATE TABLE platform_workspace (
        tenant_id   uuid PRIMARY KEY,
        name        character varying(200) NOT NULL,
        admin_email character varying(320) NULL,
        created_at  timestamp with time zone NOT NULL,
        status      character varying(20) NOT NULL DEFAULT 'active',
        CONSTRAINT ck_platform_workspace_status CHECK (status IN ('active', 'suspended'))
    );

    CREATE TABLE platform_invitation (
        id                  uuid PRIMARY KEY,
        email               character varying(320) NOT NULL,
        token_hash          character varying(128) NOT NULL,
        created_by          character varying(200) NOT NULL,
        created_at          timestamp with time zone NOT NULL,
        expires_at          timestamp with time zone NOT NULL,
        accepted_at         timestamp with time zone NULL,
        accepted_by_subject character varying(200) NULL,
        workspace_id        uuid NULL,
        revoked_at          timestamp with time zone NULL
    );

    CREATE UNIQUE INDEX ux_platform_invitation_token_hash ON platform_invitation (token_hash);
    -- At most one live (not accepted, not revoked) invitation per address.
    CREATE UNIQUE INDEX ux_platform_invitation_live_email ON platform_invitation (lower(email))
        WHERE accepted_at IS NULL AND revoked_at IS NULL;
    CREATE INDEX ix_platform_invitation_accepted_by ON platform_invitation (accepted_by_subject)
        WHERE accepted_by_subject IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261102100000_AddPlatformAdminTables') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261102100000_AddPlatformAdminTables', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261104123224_AddWorkspaceFinanceSettings') THEN
    ALTER TABLE workspace ADD budget_rates jsonb;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261104123224_AddWorkspaceFinanceSettings') THEN
    ALTER TABLE workspace ADD fiscal_year_start_month integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261104123224_AddWorkspaceFinanceSettings') THEN
    ALTER TABLE workspace ADD reporting_currency character varying(3);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261104123224_AddWorkspaceFinanceSettings') THEN
    ALTER TABLE workspace ADD CONSTRAINT ck_workspace_fiscal_year_start_month CHECK (fiscal_year_start_month IS NULL OR fiscal_year_start_month BETWEEN 1 AND 12);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261104123224_AddWorkspaceFinanceSettings') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261104123224_AddWorkspaceFinanceSettings', '10.0.4');
    END IF;
END $EF$;
COMMIT;

