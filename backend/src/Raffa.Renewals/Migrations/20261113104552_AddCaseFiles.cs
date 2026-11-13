using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Renewals.Migrations
{
    /// <inheritdoc />
    /// <summary>Case files (<c>case_file</c>): drafts, signed contracts, offers and uploaded supplier emails
    /// with their attachments, kept in object storage. Row-Level Security like every Renewals table.</summary>
    public partial class AddCaseFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "case_file",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    file_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    storage_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    draft_version = table.Column<int>(type: "integer", nullable: true),
                    vendor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reply_index = table.Column<int>(type: "integer", nullable: true),
                    source_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uploaded_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_file", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_case_file_tenant_id",
                table: "case_file",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_file_tenant_id_case_id",
                table: "case_file",
                columns: new[] { "tenant_id", "case_id" });

            // Row-Level Security, same SQL as AddRenewalCases.
            migrationBuilder.Sql(
                """
                ALTER TABLE "case_file" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "case_file" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "case_file"
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation ON "case_file";
                ALTER TABLE "case_file" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "case_file" DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "case_file");
        }
    }
}
