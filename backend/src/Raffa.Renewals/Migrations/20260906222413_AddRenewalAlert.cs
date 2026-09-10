using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Renewals.Migrations
{
    /// <summary>
    /// Task E03/F02/US01/T02 (renewal-alerts): creates this module's second tenant-scoped table,
    /// <c>renewal_alert</c> (see <see cref="Raffa.Renewals.Domain.RenewalAlert"/>'s own doc
    /// comment). Also enables Postgres Row-Level Security on that new table in the same migration —
    /// same <c>ENABLE</c> / <c>FORCE</c> / <c>CREATE POLICY</c> SQL and
    /// <c>nullif(current_setting(...), '')::uuid</c> NULL-safety guard as the existing
    /// <see cref="AddTenantRowLevelSecurity"/> migration — combined here, rather than a separate
    /// follow-up migration, since this table's RLS policy is not a retrofit onto pre-existing data
    /// (the table itself does not exist before this migration runs; same precedent
    /// <c>Raffa.Quotes.Migrations.AddSkuProductMapping</c>/<c>AddNegotiationOutcome</c> already set
    /// for this exact scenario one module over).
    /// <c>Raffa.Renewals.Tests.RenewalActionRlsMigrationCheckTests</c> (a generic, EF-model-driven
    /// check despite its own name — see that type's own doc comment) discovers this table
    /// dynamically and would fail the build without this.
    /// </summary>
    public partial class AddRenewalAlert : Migration
    {
        private const string RenewalAlertTable = "renewal_alert";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "renewal_alert",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    milestone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    threshold_days = table.Column<int>(type: "integer", nullable: false),
                    milestone_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_renewal_alert", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_renewal_alert_active_tenant_contract_milestone_threshold",
                table: "renewal_alert",
                columns: new[] { "tenant_id", "contract_id", "milestone", "threshold_days" },
                unique: true,
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_renewal_alert_tenant_id",
                table: "renewal_alert",
                column: "tenant_id");

            // ADR-009: same RLS SQL as Migrations.AddTenantRowLevelSecurity, applied to this one new
            // table (see this migration's own class doc comment for why it is combined here rather
            // than a separate follow-up migration).
            migrationBuilder.Sql(
                $"""
                ALTER TABLE "{RenewalAlertTable}" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "{RenewalAlertTable}" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "{RenewalAlertTable}"
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"""
                DROP POLICY IF EXISTS tenant_isolation ON "{RenewalAlertTable}";
                ALTER TABLE "{RenewalAlertTable}" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "{RenewalAlertTable}" DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "renewal_alert");
        }
    }
}
