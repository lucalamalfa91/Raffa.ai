CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908205020_Initial') THEN
    CREATE TABLE conversation (
        id uuid NOT NULL,
        user_id character varying(200) NOT NULL,
        title character varying(48) NOT NULL,
        scope_contract_id uuid,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_conversation PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908205020_Initial') THEN
    CREATE TABLE conversation_message (
        id uuid NOT NULL,
        conversation_id uuid NOT NULL,
        role character varying(20) NOT NULL,
        kind character varying(20) NOT NULL,
        markdown text NOT NULL,
        citations_json jsonb NOT NULL,
        actions_json jsonb NOT NULL,
        model_id character varying(200),
        prompt_version character varying(50),
        input_hash character varying(128),
        created_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_conversation_message PRIMARY KEY (id),
        CONSTRAINT fk_conversation_message_conversation_conversation_id FOREIGN KEY (conversation_id) REFERENCES conversation (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908205020_Initial') THEN
    CREATE INDEX ix_conversation_tenant_id ON conversation (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908205020_Initial') THEN
    CREATE INDEX ix_conversation_tenant_id_user_id_updated_at ON conversation (tenant_id, user_id, updated_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908205020_Initial') THEN
    CREATE INDEX ix_conversation_message_conversation_id_created_at ON conversation_message (conversation_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908205020_Initial') THEN
    CREATE INDEX ix_conversation_message_tenant_id ON conversation_message (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908205020_Initial') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260908205020_Initial', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908205121_AddTenantRowLevelSecurity') THEN
    ALTER TABLE "conversation" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "conversation" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "conversation"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908205121_AddTenantRowLevelSecurity') THEN
    ALTER TABLE "conversation_message" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "conversation_message" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "conversation_message"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908205121_AddTenantRowLevelSecurity') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260908205121_AddTenantRowLevelSecurity', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922150000_AddInterviewJson') THEN
    ALTER TABLE conversation_message ADD interview_json jsonb;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922150000_AddInterviewJson') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260922150000_AddInterviewJson', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922160000_AddWebResearchUsage') THEN
    CREATE TABLE chat_web_research_usage (
        tenant_id uuid NOT NULL,
        day date NOT NULL,
        calls integer NOT NULL,
        CONSTRAINT pk_chat_web_research_usage PRIMARY KEY (tenant_id, day)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922160000_AddWebResearchUsage') THEN
    ALTER TABLE "chat_web_research_usage" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "chat_web_research_usage" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "chat_web_research_usage"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922160000_AddWebResearchUsage') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260922160000_AddWebResearchUsage', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922171713_AddDraftPayloadAndFeatureRequest') THEN
    ALTER TABLE conversation_message ADD payload_json jsonb;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922171713_AddDraftPayloadAndFeatureRequest') THEN
    CREATE TABLE feature_request (
        id uuid NOT NULL,
        conversation_id uuid NOT NULL,
        message_id uuid NOT NULL,
        user_id character varying(200) NOT NULL,
        gap_key character varying(40) NOT NULL,
        gap_title character varying(120) NOT NULL,
        language character varying(5) NOT NULL,
        answers_json jsonb NOT NULL,
        environment character varying(20) NOT NULL,
        workspace_hash character varying(16) NOT NULL,
        status character varying(20) NOT NULL,
        issue_number integer,
        issue_url character varying(300),
        publish_error character varying(500),
        created_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_feature_request PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922171713_AddDraftPayloadAndFeatureRequest') THEN
    CREATE INDEX ix_feature_request_tenant_id ON feature_request (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922171713_AddDraftPayloadAndFeatureRequest') THEN
    CREATE UNIQUE INDEX ix_feature_request_tenant_id_message_id ON feature_request (tenant_id, message_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922171713_AddDraftPayloadAndFeatureRequest') THEN
    ALTER TABLE "feature_request" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "feature_request" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "feature_request"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922171713_AddDraftPayloadAndFeatureRequest') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260922171713_AddDraftPayloadAndFeatureRequest', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260923125212_AddConversationCustomTitle') THEN
    ALTER TABLE conversation ADD custom_title character varying(48);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260923125212_AddConversationCustomTitle') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260923125212_AddConversationCustomTitle', '10.0.4');
    END IF;
END $EF$;
COMMIT;

