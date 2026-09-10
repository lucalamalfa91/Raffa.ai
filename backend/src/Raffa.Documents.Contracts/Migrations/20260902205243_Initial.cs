using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Raffa.Documents.Contracts.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "contract",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: true),
                    cancellation_deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    annual_spend = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    total_contract_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    auto_renewal = table.Column<bool>(type: "boolean", nullable: false),
                    renewal_term_months = table.Column<int>(type: "integer", nullable: true),
                    payment_terms = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    governing_law = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_contract_parent_contract_id",
                        column: x => x.parent_contract_id,
                        principalTable: "contract",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "correction_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    previous_value = table.Column<string>(type: "text", nullable: true),
                    new_value = table.Column<string>(type: "text", nullable: true),
                    corrected_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    corrected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_correction_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "embedding",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    chunk_text = table.Column<string>(type: "text", nullable: false),
                    vector = table.Column<Vector>(type: "vector(1536)", nullable: false),
                    model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_embedding", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contract_version",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    change_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_version", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_version_contract_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contract",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    document_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    storage_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    checksum = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    processing_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document", x => x.id);
                    table.ForeignKey(
                        name: "fk_document_contract_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contract",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "clause",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    clause_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    raw_text = table.Column<string>(type: "text", nullable: false),
                    normalized_value = table.Column<string>(type: "text", nullable: true),
                    risk_level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    source_span = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source_page = table.Column<int>(type: "integer", nullable: true),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clause", x => x.id);
                    table.ForeignKey(
                        name: "fk_clause_contract_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contract",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_clause_document_source_document_id",
                        column: x => x.source_document_id,
                        principalTable: "document",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_version",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    storage_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    checksum = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_version", x => x.id);
                    table.ForeignKey(
                        name: "fk_document_version_document_document_id",
                        column: x => x.document_id,
                        principalTable: "document",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "extraction_job",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    model_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_detail = table.Column<string>(type: "text", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extraction_job", x => x.id);
                    table.ForeignKey(
                        name: "fk_extraction_job_document_document_id",
                        column: x => x.document_id,
                        principalTable: "document",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "obligation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    party = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    obligation_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    recurrence_rule = table.Column<string>(type: "text", nullable: true),
                    criticality = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_obligation", x => x.id);
                    table.ForeignKey(
                        name: "fk_obligation_contract_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contract",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_obligation_document_source_document_id",
                        column: x => x.source_document_id,
                        principalTable: "document",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "risk",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clause_id = table.Column<Guid>(type: "uuid", nullable: true),
                    risk_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    identified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_risk", x => x.id);
                    table.ForeignKey(
                        name: "fk_risk_clause_clause_id",
                        column: x => x.clause_id,
                        principalTable: "clause",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_risk_contract_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contract",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_clause_contract_id",
                table: "clause",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_clause_source_document_id",
                table: "clause",
                column: "source_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_clause_tenant_id",
                table: "clause",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_parent_contract_id",
                table: "contract",
                column: "parent_contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_supplier_id",
                table: "contract",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_tenant_id",
                table: "contract",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_version_contract_id_version_number",
                table: "contract_version",
                columns: new[] { "contract_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contract_version_tenant_id",
                table: "contract_version",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_correction_history_target_entity_type_target_entity_id",
                table: "correction_history",
                columns: new[] { "target_entity_type", "target_entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_correction_history_tenant_id",
                table: "correction_history",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_contract_id",
                table: "document",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_tenant_id",
                table: "document",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_version_document_id_version_number",
                table: "document_version",
                columns: new[] { "document_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_version_tenant_id",
                table: "document_version",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_embedding_source_type_source_id",
                table: "embedding",
                columns: new[] { "source_type", "source_id" });

            migrationBuilder.CreateIndex(
                name: "ix_embedding_tenant_id",
                table: "embedding",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_extraction_job_document_id",
                table: "extraction_job",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_extraction_job_status",
                table: "extraction_job",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_extraction_job_tenant_id",
                table: "extraction_job",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_obligation_contract_id",
                table: "obligation",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_obligation_due_date",
                table: "obligation",
                column: "due_date");

            migrationBuilder.CreateIndex(
                name: "ix_obligation_source_document_id",
                table: "obligation",
                column: "source_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_obligation_tenant_id",
                table: "obligation",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_risk_clause_id",
                table: "risk",
                column: "clause_id");

            migrationBuilder.CreateIndex(
                name: "ix_risk_contract_id",
                table: "risk",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_risk_tenant_id",
                table: "risk",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contract_version");

            migrationBuilder.DropTable(
                name: "correction_history");

            migrationBuilder.DropTable(
                name: "document_version");

            migrationBuilder.DropTable(
                name: "embedding");

            migrationBuilder.DropTable(
                name: "extraction_job");

            migrationBuilder.DropTable(
                name: "obligation");

            migrationBuilder.DropTable(
                name: "risk");

            migrationBuilder.DropTable(
                name: "clause");

            migrationBuilder.DropTable(
                name: "document");

            migrationBuilder.DropTable(
                name: "contract");
        }
    }
}
