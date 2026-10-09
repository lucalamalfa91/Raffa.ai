using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Documents.Contracts.Migrations
{
    /// <inheritdoc />
    public partial class AddExtractionRunCheckpointNoticePeriodAndDocumentChecksumIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "extraction_run_id",
                table: "risk",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "extraction_run_id",
                table: "obligation",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "extracted_count",
                table: "extraction_job",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "extraction_run_id",
                table: "extraction_job",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "failure_kind",
                table: "extraction_job",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "input_hash",
                table: "extraction_job",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "skipped_count",
                table: "extraction_job",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "extraction_run_id",
                table: "extraction_evidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "extraction_run_id",
                table: "contract_line_item",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "notice_period_days",
                table: "contract",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "extraction_run_id",
                table: "clause",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_risk_extraction_run_id",
                table: "risk",
                column: "extraction_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_obligation_extraction_run_id",
                table: "obligation",
                column: "extraction_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_extraction_job_extraction_run_id",
                table: "extraction_job",
                column: "extraction_run_id");

            // F5-D01: one document per (tenant, checksum). Written as SQL, not CreateIndex, so that the
            // migration cannot fail on data: the old upload path never checked, so a database may
            // already hold the same file twice. The model's index (see DocumentConfiguration) is
            // partial from a fixed instant; if rows created since that instant already collide, the
            // enforced window starts at the moment this migration runs instead. The upload service
            // answers "already uploaded" for every earlier duplicate either way.
            //
            // Three plain statements (procedure, call, drop) because the idempotent deploy script
            // embeds each Sql operation inside a plpgsql block, where a DO statement is not allowed
            // but CREATE PROCEDURE / CALL are, and the same text must also run as-is through
            // Database.MigrateAsync().
            migrationBuilder.Sql(
                """
                CREATE PROCEDURE ux_document_checksum_setup() LANGUAGE plpgsql AS $f$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM document
                        WHERE processing_status <> 'Rejected' AND created_at >= '2026-10-08T00:00:00+00'
                        GROUP BY tenant_id, checksum HAVING count(*) > 1) THEN
                        EXECUTE format(
                            'CREATE UNIQUE INDEX ux_document_tenant_checksum ON document (tenant_id, checksum) WHERE processing_status <> ''Rejected'' AND created_at >= %L',
                            now());
                    ELSE
                        CREATE UNIQUE INDEX ux_document_tenant_checksum ON document (tenant_id, checksum)
                            WHERE processing_status <> 'Rejected' AND created_at >= '2026-10-08T00:00:00+00';
                    END IF;
                END
                $f$;
                """);
            migrationBuilder.Sql("CALL ux_document_checksum_setup();");
            migrationBuilder.Sql("DROP PROCEDURE ux_document_checksum_setup();");

            migrationBuilder.CreateIndex(
                name: "ix_contract_line_item_extraction_run_id",
                table: "contract_line_item",
                column: "extraction_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_clause_extraction_run_id",
                table: "clause",
                column: "extraction_run_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_risk_extraction_run_id",
                table: "risk");

            migrationBuilder.DropIndex(
                name: "ix_obligation_extraction_run_id",
                table: "obligation");

            migrationBuilder.DropIndex(
                name: "ix_extraction_job_extraction_run_id",
                table: "extraction_job");

            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_document_tenant_checksum;");

            migrationBuilder.DropIndex(
                name: "ix_contract_line_item_extraction_run_id",
                table: "contract_line_item");

            migrationBuilder.DropIndex(
                name: "ix_clause_extraction_run_id",
                table: "clause");

            migrationBuilder.DropColumn(
                name: "extraction_run_id",
                table: "risk");

            migrationBuilder.DropColumn(
                name: "extraction_run_id",
                table: "obligation");

            migrationBuilder.DropColumn(
                name: "extracted_count",
                table: "extraction_job");

            migrationBuilder.DropColumn(
                name: "extraction_run_id",
                table: "extraction_job");

            migrationBuilder.DropColumn(
                name: "failure_kind",
                table: "extraction_job");

            migrationBuilder.DropColumn(
                name: "input_hash",
                table: "extraction_job");

            migrationBuilder.DropColumn(
                name: "skipped_count",
                table: "extraction_job");

            migrationBuilder.DropColumn(
                name: "extraction_run_id",
                table: "extraction_evidence");

            migrationBuilder.DropColumn(
                name: "extraction_run_id",
                table: "contract_line_item");

            migrationBuilder.DropColumn(
                name: "notice_period_days",
                table: "contract");

            migrationBuilder.DropColumn(
                name: "extraction_run_id",
                table: "clause");
        }
    }
}
