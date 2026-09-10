using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Quotes.Migrations
{
    /// <summary>
    /// Task E05/F03/US02/T01 (negotiation-outcome): creates this module's fourth tenant-scoped
    /// table, <c>negotiation_outcome</c> (see <see cref="Raffa.Quotes.Domain.NegotiationOutcome"/>'s
    /// own doc comment). Also enables Postgres Row-Level Security on that new table in the same
    /// migration — same <c>ENABLE</c> / <c>FORCE</c> / <c>CREATE POLICY</c> SQL and
    /// <c>nullif(current_setting(...), '')::uuid</c> NULL-safety guard as the existing
    /// <see cref="AddTenantRowLevelSecurity"/>/<see cref="AddSkuProductMapping"/> migrations —
    /// combined here, rather than a separate follow-up migration, since this table's RLS policy is
    /// not a retrofit onto pre-existing data (the table itself does not exist before this migration
    /// runs). <see cref="Raffa.Quotes.Tests.QuoteRlsMigrationCheckTests"/> discovers this table
    /// dynamically from the EF model and would fail the build without this.
    /// </summary>
    public partial class AddNegotiationOutcome : Migration
    {
        private const string NegotiationOutcomeTable = "negotiation_outcome";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "negotiation_outcome",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    quote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_quote_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    target_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    final_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    realized_saving = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    negotiation_duration_days = table.Column<int>(type: "integer", nullable: false),
                    levers_used = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_negotiation_outcome", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_negotiation_outcome_tenant_id",
                table: "negotiation_outcome",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_negotiation_outcome_tenant_id_quote_id",
                table: "negotiation_outcome",
                columns: new[] { "tenant_id", "quote_id" });

            // ADR-009: same RLS SQL as Migrations.AddTenantRowLevelSecurity/AddSkuProductMapping,
            // applied to this one new table (see this migration's own class doc comment for why it
            // is combined here rather than a separate follow-up migration).
            migrationBuilder.Sql(
                $"""
                ALTER TABLE "{NegotiationOutcomeTable}" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "{NegotiationOutcomeTable}" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "{NegotiationOutcomeTable}"
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"""
                DROP POLICY IF EXISTS tenant_isolation ON "{NegotiationOutcomeTable}";
                ALTER TABLE "{NegotiationOutcomeTable}" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "{NegotiationOutcomeTable}" DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "negotiation_outcome");
        }
    }
}
