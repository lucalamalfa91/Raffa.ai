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

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260906222413_AddRenewalAlert') THEN
    CREATE TABLE renewal_alert (
        id uuid NOT NULL,
        contract_id uuid NOT NULL,
        milestone character varying(30) NOT NULL,
        threshold_days integer NOT NULL,
        milestone_date date NOT NULL,
        status character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_renewal_alert PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260906222413_AddRenewalAlert') THEN
    CREATE UNIQUE INDEX ix_renewal_alert_active_tenant_contract_milestone_threshold ON renewal_alert (tenant_id, contract_id, milestone, threshold_days) WHERE status = 'Active';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260906222413_AddRenewalAlert') THEN
    CREATE INDEX ix_renewal_alert_tenant_id ON renewal_alert (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260906222413_AddRenewalAlert') THEN
    ALTER TABLE "renewal_alert" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "renewal_alert" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "renewal_alert"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260906222413_AddRenewalAlert') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260906222413_AddRenewalAlert', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260917131939_AddRenewalNegotiationTodo') THEN
    CREATE TABLE renewal_negotiation_todo (
        id uuid NOT NULL,
        contract_id uuid NOT NULL,
        point_key character varying(200) NOT NULL,
        topic character varying(200) NOT NULL,
        rank integer NOT NULL,
        current text NOT NULL,
        target text NOT NULL,
        rationale text NOT NULL,
        citation_keys text NOT NULL,
        source character varying(20) NOT NULL,
        status character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_renewal_negotiation_todo PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260917131939_AddRenewalNegotiationTodo') THEN
    CREATE UNIQUE INDEX ix_renewal_negotiation_todo_tenant_contract_point_key ON renewal_negotiation_todo (tenant_id, contract_id, point_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260917131939_AddRenewalNegotiationTodo') THEN
    CREATE INDEX ix_renewal_negotiation_todo_tenant_id ON renewal_negotiation_todo (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260917131939_AddRenewalNegotiationTodo') THEN
    ALTER TABLE "renewal_negotiation_todo" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "renewal_negotiation_todo" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "renewal_negotiation_todo"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260917131939_AddRenewalNegotiationTodo') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260917131939_AddRenewalNegotiationTodo', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    CREATE TABLE case_delegation (
        id uuid NOT NULL,
        case_id uuid NOT NULL,
        user_id uuid,
        user_name character varying(200) NOT NULL,
        can_approve boolean NOT NULL,
        can_close boolean NOT NULL,
        granted_by character varying(200),
        granted_at timestamp with time zone NOT NULL,
        revoked_at timestamp with time zone,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_case_delegation PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    CREATE TABLE case_email (
        id uuid NOT NULL,
        case_id uuid NOT NULL,
        email_index integer NOT NULL,
        kind character varying(40) NOT NULL,
        version integer NOT NULL,
        state character varying(20) NOT NULL,
        to_address character varying(320),
        subject text NOT NULL,
        body text NOT NULL,
        attachments_json jsonb,
        approved_version integer,
        approved_by character varying(200),
        approved_at timestamp with time zone,
        copied_at timestamp with time zone,
        sent_by character varying(200),
        sent_at timestamp with time zone,
        sent_body text,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_case_email PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    CREATE TABLE case_event (
        id uuid NOT NULL,
        case_id uuid NOT NULL,
        at timestamp with time zone NOT NULL,
        actor character varying(200) NOT NULL,
        type character varying(60) NOT NULL,
        payload_json jsonb,
        email_index integer,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_case_event PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    CREATE TABLE case_vendor_message (
        id uuid NOT NULL,
        case_id uuid NOT NULL,
        direction character varying(10) NOT NULL,
        received_at timestamp with time zone NOT NULL,
        pasted_text text NOT NULL,
        content_hash character varying(64) NOT NULL,
        thread_key character varying(200),
        parsed_json jsonb,
        analysed_by_model character varying(200),
        prompt_version character varying(60),
        input_hash character varying(64),
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_case_vendor_message PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    CREATE TABLE renewal_case (
        id uuid NOT NULL,
        contract_id uuid NOT NULL,
        owner_user_id uuid,
        approver_user_id uuid,
        phase character varying(40) NOT NULL,
        step character varying(40) NOT NULL,
        status character varying(40) NOT NULL,
        branch character varying(20) NOT NULL,
        state jsonb NOT NULL,
        version integer NOT NULL,
        notice_deadline date,
        contract_end date,
        outcome_kind character varying(20),
        outcome_json jsonb,
        opened_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        closed_at timestamp with time zone,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_renewal_case PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    CREATE TABLE today_snooze (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        item_key character varying(200) NOT NULL,
        until date NOT NULL,
        created_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_today_snooze PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    CREATE TABLE user_reminder (
        id uuid NOT NULL,
        case_id uuid,
        reminder_index integer,
        user_id uuid,
        user_name character varying(200) NOT NULL,
        kind character varying(20) NOT NULL,
        due_date date NOT NULL,
        state character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_user_reminder PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    CREATE INDEX ix_case_delegation_tenant_id ON case_delegation (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    CREATE INDEX ix_case_delegation_tenant_id_case_id ON case_delegation (tenant_id, case_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    CREATE INDEX ix_case_email_tenant_id ON case_email (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    CREATE UNIQUE INDEX ix_case_email_tenant_id_case_id_email_index ON case_email (tenant_id, case_id, email_index);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    CREATE INDEX ix_case_event_tenant_id ON case_event (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    CREATE INDEX ix_case_event_tenant_id_case_id_at ON case_event (tenant_id, case_id, at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    CREATE INDEX ix_case_vendor_message_tenant_id ON case_vendor_message (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    CREATE UNIQUE INDEX ix_case_vendor_message_tenant_id_case_id_content_hash ON case_vendor_message (tenant_id, case_id, content_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    CREATE INDEX ix_renewal_case_tenant_id ON renewal_case (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    CREATE UNIQUE INDEX ux_renewal_case_tenant_contract_open ON renewal_case (tenant_id, contract_id) WHERE closed_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    CREATE INDEX ix_today_snooze_tenant_id ON today_snooze (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    CREATE UNIQUE INDEX ix_today_snooze_tenant_id_user_id_item_key ON today_snooze (tenant_id, user_id, item_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    CREATE INDEX ix_user_reminder_tenant_id ON user_reminder (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    CREATE INDEX ix_user_reminder_tenant_id_user_id_due_date ON user_reminder (tenant_id, user_id, due_date);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    ALTER TABLE "renewal_case" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "renewal_case" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "renewal_case"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    ALTER TABLE "case_email" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "case_email" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "case_email"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    ALTER TABLE "case_delegation" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "case_delegation" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "case_delegation"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    ALTER TABLE "case_event" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "case_event" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "case_event"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    ALTER TABLE "case_vendor_message" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "case_vendor_message" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "case_vendor_message"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    ALTER TABLE "user_reminder" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "user_reminder" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "user_reminder"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    ALTER TABLE "today_snooze" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "today_snooze" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "today_snooze"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261101120403_AddRenewalCases') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261101120403_AddRenewalCases', '10.0.4');
    END IF;
END $EF$;
COMMIT;

