using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Renewals.Migrations
{
    /// <inheritdoc />
    /// <summary>Company rules (Impostazioni): one row per tenant — the rules text and the alerting
    /// window taken from it. Row-Level Security like every Renewals table.</summary>
    public partial class AddCompanyRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    start_window_days = table.Column<int>(type: "integer", nullable: false),
                    source_file = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    updated_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_rules", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_company_rules_tenant_id",
                table: "company_rules",
                column: "tenant_id",
                unique: true);

            // Row-Level Security, same SQL as AddRenewalCases.
            migrationBuilder.Sql(
                """
                ALTER TABLE "company_rules" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "company_rules" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "company_rules"
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation ON "company_rules";
                ALTER TABLE "company_rules" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "company_rules" DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "company_rules");
        }
    }
}
