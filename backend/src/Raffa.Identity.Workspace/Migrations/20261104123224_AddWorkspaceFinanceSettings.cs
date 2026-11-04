using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Identity.Workspace.Migrations
{
    /// <summary>
    /// Spesa e risparmi: three nullable columns on <c>workspace</c>, all set by the Admin --
    /// <c>reporting_currency</c> (ISO 4217; null: the workspace's own currency),
    /// <c>fiscal_year_start_month</c> (1-12, checked; null: not set, reported as a calendar year) and
    /// <c>budget_rates</c> (jsonb, units of the reporting currency per unit of each currency; null:
    /// none, so no contract in another currency is converted). Nullable on purpose: "not set" is not
    /// a value, as for the profile columns. No RLS change: <c>workspace</c> already carries its
    /// <c>tenant_isolation</c> policy.
    /// </summary>
    public partial class AddWorkspaceFinanceSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "budget_rates",
                table: "workspace",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "fiscal_year_start_month",
                table: "workspace",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reporting_currency",
                table: "workspace",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_workspace_fiscal_year_start_month",
                table: "workspace",
                sql: "fiscal_year_start_month IS NULL OR fiscal_year_start_month BETWEEN 1 AND 12");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_workspace_fiscal_year_start_month",
                table: "workspace");

            migrationBuilder.DropColumn(
                name: "budget_rates",
                table: "workspace");

            migrationBuilder.DropColumn(
                name: "fiscal_year_start_month",
                table: "workspace");

            migrationBuilder.DropColumn(
                name: "reporting_currency",
                table: "workspace");
        }
    }
}
