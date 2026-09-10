CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE EXTENSION IF NOT EXISTS vector;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE TABLE contract (
        id uuid NOT NULL,
        supplier_id uuid,
        parent_contract_id uuid,
        type character varying(50) NOT NULL,
        status character varying(50) NOT NULL,
        currency character varying(3) NOT NULL,
        start_date date,
        end_date date,
        effective_date date,
        cancellation_deadline date,
        annual_spend numeric(18,2),
        total_contract_value numeric(18,2),
        auto_renewal boolean NOT NULL,
        renewal_term_months integer,
        payment_terms character varying(500),
        governing_law character varying(200),
        created_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_contract PRIMARY KEY (id),
        CONSTRAINT fk_contract_contract_parent_contract_id FOREIGN KEY (parent_contract_id) REFERENCES contract (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE TABLE correction_history (
        id uuid NOT NULL,
        target_entity_type character varying(100) NOT NULL,
        target_entity_id uuid NOT NULL,
        field_name character varying(200) NOT NULL,
        previous_value text,
        new_value text,
        corrected_by character varying(200) NOT NULL,
        corrected_at timestamp with time zone NOT NULL,
        reason character varying(1000),
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_correction_history PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE TABLE embedding (
        id uuid NOT NULL,
        source_type character varying(100) NOT NULL,
        source_id uuid NOT NULL,
        chunk_index integer NOT NULL,
        chunk_text text NOT NULL,
        vector vector(1536) NOT NULL,
        model character varying(200) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_embedding PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE TABLE contract_version (
        id uuid NOT NULL,
        contract_id uuid NOT NULL,
        version_number integer NOT NULL,
        snapshot_json jsonb NOT NULL,
        change_reason character varying(1000),
        created_by character varying(200) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_contract_version PRIMARY KEY (id),
        CONSTRAINT fk_contract_version_contract_contract_id FOREIGN KEY (contract_id) REFERENCES contract (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE TABLE document (
        id uuid NOT NULL,
        contract_id uuid,
        file_name character varying(500) NOT NULL,
        mime_type character varying(200) NOT NULL,
        document_type character varying(50) NOT NULL,
        storage_path character varying(1000) NOT NULL,
        checksum character varying(128) NOT NULL,
        processing_status character varying(30) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_document PRIMARY KEY (id),
        CONSTRAINT fk_document_contract_contract_id FOREIGN KEY (contract_id) REFERENCES contract (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE TABLE clause (
        id uuid NOT NULL,
        contract_id uuid NOT NULL,
        source_document_id uuid,
        clause_type character varying(100) NOT NULL,
        raw_text text NOT NULL,
        normalized_value text,
        risk_level character varying(20),
        source_span character varying(500),
        source_page integer,
        confidence double precision,
        created_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_clause PRIMARY KEY (id),
        CONSTRAINT fk_clause_contract_contract_id FOREIGN KEY (contract_id) REFERENCES contract (id) ON DELETE CASCADE,
        CONSTRAINT fk_clause_document_source_document_id FOREIGN KEY (source_document_id) REFERENCES document (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE TABLE document_version (
        id uuid NOT NULL,
        document_id uuid NOT NULL,
        version_number integer NOT NULL,
        storage_path character varying(1000) NOT NULL,
        checksum character varying(128) NOT NULL,
        created_by character varying(200) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_document_version PRIMARY KEY (id),
        CONSTRAINT fk_document_version_document_document_id FOREIGN KEY (document_id) REFERENCES document (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE TABLE extraction_job (
        id uuid NOT NULL,
        document_id uuid NOT NULL,
        stage character varying(50) NOT NULL,
        status character varying(30) NOT NULL,
        model_id character varying(200),
        queued_at timestamp with time zone NOT NULL,
        started_at timestamp with time zone,
        completed_at timestamp with time zone,
        error_detail text,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_extraction_job PRIMARY KEY (id),
        CONSTRAINT fk_extraction_job_document_document_id FOREIGN KEY (document_id) REFERENCES document (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE TABLE obligation (
        id uuid NOT NULL,
        contract_id uuid NOT NULL,
        source_document_id uuid,
        party character varying(300) NOT NULL,
        obligation_type character varying(100) NOT NULL,
        description text NOT NULL,
        due_date date,
        recurrence_rule text,
        criticality character varying(30),
        status character varying(30),
        confidence double precision,
        created_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_obligation PRIMARY KEY (id),
        CONSTRAINT fk_obligation_contract_contract_id FOREIGN KEY (contract_id) REFERENCES contract (id) ON DELETE CASCADE,
        CONSTRAINT fk_obligation_document_source_document_id FOREIGN KEY (source_document_id) REFERENCES document (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE TABLE risk (
        id uuid NOT NULL,
        contract_id uuid NOT NULL,
        clause_id uuid,
        risk_type character varying(100) NOT NULL,
        severity character varying(20) NOT NULL,
        description text NOT NULL,
        confidence double precision,
        status character varying(30),
        identified_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_risk PRIMARY KEY (id),
        CONSTRAINT fk_risk_clause_clause_id FOREIGN KEY (clause_id) REFERENCES clause (id) ON DELETE RESTRICT,
        CONSTRAINT fk_risk_contract_contract_id FOREIGN KEY (contract_id) REFERENCES contract (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE INDEX ix_clause_contract_id ON clause (contract_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE INDEX ix_clause_source_document_id ON clause (source_document_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE INDEX ix_clause_tenant_id ON clause (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE INDEX ix_contract_parent_contract_id ON contract (parent_contract_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE INDEX ix_contract_supplier_id ON contract (supplier_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE INDEX ix_contract_tenant_id ON contract (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE UNIQUE INDEX ix_contract_version_contract_id_version_number ON contract_version (contract_id, version_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE INDEX ix_contract_version_tenant_id ON contract_version (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE INDEX ix_correction_history_target_entity_type_target_entity_id ON correction_history (target_entity_type, target_entity_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE INDEX ix_correction_history_tenant_id ON correction_history (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE INDEX ix_document_contract_id ON document (contract_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE INDEX ix_document_tenant_id ON document (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE UNIQUE INDEX ix_document_version_document_id_version_number ON document_version (document_id, version_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE INDEX ix_document_version_tenant_id ON document_version (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE INDEX ix_embedding_source_type_source_id ON embedding (source_type, source_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE INDEX ix_embedding_tenant_id ON embedding (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE INDEX ix_extraction_job_document_id ON extraction_job (document_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE INDEX ix_extraction_job_status ON extraction_job (status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE INDEX ix_extraction_job_tenant_id ON extraction_job (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE INDEX ix_obligation_contract_id ON obligation (contract_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE INDEX ix_obligation_due_date ON obligation (due_date);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE INDEX ix_obligation_source_document_id ON obligation (source_document_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE INDEX ix_obligation_tenant_id ON obligation (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE INDEX ix_risk_clause_id ON risk (clause_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE INDEX ix_risk_contract_id ON risk (contract_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    CREATE INDEX ix_risk_tenant_id ON risk (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902205243_Initial') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260902205243_Initial', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903080549_AddTenantRowLevelSecurity') THEN
    ALTER TABLE "contract" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "contract" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "contract"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903080549_AddTenantRowLevelSecurity') THEN
    ALTER TABLE "contract_version" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "contract_version" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "contract_version"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903080549_AddTenantRowLevelSecurity') THEN
    ALTER TABLE "correction_history" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "correction_history" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "correction_history"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903080549_AddTenantRowLevelSecurity') THEN
    ALTER TABLE "document" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "document" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "document"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903080549_AddTenantRowLevelSecurity') THEN
    ALTER TABLE "document_version" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "document_version" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "document_version"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903080549_AddTenantRowLevelSecurity') THEN
    ALTER TABLE "extraction_job" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "extraction_job" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "extraction_job"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903080549_AddTenantRowLevelSecurity') THEN
    ALTER TABLE "clause" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "clause" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "clause"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903080549_AddTenantRowLevelSecurity') THEN
    ALTER TABLE "obligation" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "obligation" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "obligation"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903080549_AddTenantRowLevelSecurity') THEN
    ALTER TABLE "risk" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "risk" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "risk"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903080549_AddTenantRowLevelSecurity') THEN
    ALTER TABLE "embedding" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "embedding" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "embedding"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903080549_AddTenantRowLevelSecurity') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260903080549_AddTenantRowLevelSecurity', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904093959_AddContractLineItem') THEN
    CREATE TABLE contract_line_item (
        id uuid NOT NULL,
        contract_id uuid NOT NULL,
        product_id uuid,
        sku character varying(200),
        description character varying(1000) NOT NULL,
        quantity numeric(18,4),
        unit character varying(50),
        unit_price numeric(18,2),
        list_price numeric(18,2),
        discount numeric(5,2),
        billing_period character varying(50),
        annual_cost numeric(18,2),
        total_cost numeric(18,2),
        created_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_contract_line_item PRIMARY KEY (id),
        CONSTRAINT fk_contract_line_item_contract_contract_id FOREIGN KEY (contract_id) REFERENCES contract (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904093959_AddContractLineItem') THEN
    CREATE INDEX ix_contract_line_item_contract_id ON contract_line_item (contract_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904093959_AddContractLineItem') THEN
    CREATE INDEX ix_contract_line_item_product_id ON contract_line_item (product_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904093959_AddContractLineItem') THEN
    CREATE INDEX ix_contract_line_item_tenant_id ON contract_line_item (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904093959_AddContractLineItem') THEN
    ALTER TABLE "contract_line_item" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "contract_line_item" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "contract_line_item"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904093959_AddContractLineItem') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260904093959_AddContractLineItem', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904133500_AddEvidenceConfidenceVersionColumns') THEN
    ALTER TABLE risk ADD source_document_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904133500_AddEvidenceConfidenceVersionColumns') THEN
    ALTER TABLE risk ADD source_page integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904133500_AddEvidenceConfidenceVersionColumns') THEN
    ALTER TABLE risk ADD source_span character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904133500_AddEvidenceConfidenceVersionColumns') THEN
    ALTER TABLE risk ADD version integer NOT NULL DEFAULT 1;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904133500_AddEvidenceConfidenceVersionColumns') THEN
    ALTER TABLE obligation ADD source_page integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904133500_AddEvidenceConfidenceVersionColumns') THEN
    ALTER TABLE obligation ADD source_span character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904133500_AddEvidenceConfidenceVersionColumns') THEN
    ALTER TABLE obligation ADD version integer NOT NULL DEFAULT 1;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904133500_AddEvidenceConfidenceVersionColumns') THEN
    ALTER TABLE contract_line_item ADD confidence double precision;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904133500_AddEvidenceConfidenceVersionColumns') THEN
    ALTER TABLE contract_line_item ADD source_document_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904133500_AddEvidenceConfidenceVersionColumns') THEN
    ALTER TABLE contract_line_item ADD source_page integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904133500_AddEvidenceConfidenceVersionColumns') THEN
    ALTER TABLE contract_line_item ADD source_span character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904133500_AddEvidenceConfidenceVersionColumns') THEN
    ALTER TABLE contract_line_item ADD version integer NOT NULL DEFAULT 1;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904133500_AddEvidenceConfidenceVersionColumns') THEN
    ALTER TABLE contract ADD version integer NOT NULL DEFAULT 1;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904133500_AddEvidenceConfidenceVersionColumns') THEN
    ALTER TABLE clause ADD version integer NOT NULL DEFAULT 1;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904133500_AddEvidenceConfidenceVersionColumns') THEN
    CREATE INDEX ix_risk_source_document_id ON risk (source_document_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904133500_AddEvidenceConfidenceVersionColumns') THEN
    CREATE INDEX ix_contract_line_item_source_document_id ON contract_line_item (source_document_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904133500_AddEvidenceConfidenceVersionColumns') THEN
    ALTER TABLE contract_line_item ADD CONSTRAINT fk_contract_line_item_document_source_document_id FOREIGN KEY (source_document_id) REFERENCES document (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904133500_AddEvidenceConfidenceVersionColumns') THEN
    ALTER TABLE risk ADD CONSTRAINT fk_risk_document_source_document_id FOREIGN KEY (source_document_id) REFERENCES document (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904133500_AddEvidenceConfidenceVersionColumns') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260904133500_AddEvidenceConfidenceVersionColumns', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904135028_AddStagedExtractionEvidence') THEN
    CREATE TABLE extraction_evidence (
        id uuid NOT NULL,
        contract_id uuid NOT NULL,
        source_document_id uuid,
        extraction_job_id uuid,
        field_name character varying(200) NOT NULL,
        value text,
        source_span character varying(500),
        source_page integer,
        confidence double precision,
        created_at timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_extraction_evidence PRIMARY KEY (id),
        CONSTRAINT fk_extraction_evidence_contract_contract_id FOREIGN KEY (contract_id) REFERENCES contract (id) ON DELETE CASCADE,
        CONSTRAINT fk_extraction_evidence_document_source_document_id FOREIGN KEY (source_document_id) REFERENCES document (id) ON DELETE RESTRICT,
        CONSTRAINT fk_extraction_evidence_extraction_job_extraction_job_id FOREIGN KEY (extraction_job_id) REFERENCES extraction_job (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904135028_AddStagedExtractionEvidence') THEN
    CREATE INDEX ix_extraction_evidence_contract_id_field_name ON extraction_evidence (contract_id, field_name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904135028_AddStagedExtractionEvidence') THEN
    CREATE INDEX ix_extraction_evidence_extraction_job_id ON extraction_evidence (extraction_job_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904135028_AddStagedExtractionEvidence') THEN
    CREATE INDEX ix_extraction_evidence_source_document_id ON extraction_evidence (source_document_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904135028_AddStagedExtractionEvidence') THEN
    CREATE INDEX ix_extraction_evidence_tenant_id ON extraction_evidence (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904135028_AddStagedExtractionEvidence') THEN
    ALTER TABLE "extraction_evidence" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "extraction_evidence" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON "extraction_evidence"
        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904135028_AddStagedExtractionEvidence') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260904135028_AddStagedExtractionEvidence', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260909115908_AddDocumentPreviewAndEmbeddingPage') THEN
    ALTER TABLE embedding ADD page integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260909115908_AddDocumentPreviewAndEmbeddingPage') THEN
    ALTER TABLE embedding ADD section character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260909115908_AddDocumentPreviewAndEmbeddingPage') THEN
    ALTER TABLE document ADD page_count integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260909115908_AddDocumentPreviewAndEmbeddingPage') THEN
    ALTER TABLE document ADD preview_path character varying(1000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260909115908_AddDocumentPreviewAndEmbeddingPage') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260909115908_AddDocumentPreviewAndEmbeddingPage', '10.0.4');
    END IF;
END $EF$;
COMMIT;

