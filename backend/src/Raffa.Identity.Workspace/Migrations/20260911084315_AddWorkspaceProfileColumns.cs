using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Identity.Workspace.Migrations
{
    /// <summary>
    /// Task E14/F01/US01/T02 (story us-01-identity-scoped-rls AC-5; NW-24; ADR-003 w14 amendment
    /// footer clause 1): three columns on <c>workspace</c> -- <c>industry</c>, <c>country</c>
    /// (ISO 3166-1 alpha-2) and <c>currency</c> (ISO 4217 alpha-3, derived from <c>country</c> by a
    /// later task, never typed) -- all nullable. Existing rows predate these columns and a NOT
    /// NULL column on a populated table would need a fabricated default (the
    /// `QuoteLineConfiguration.cs:45-49` trap); see <see cref="Raffa.Identity.Workspace.Domain.WorkspaceTenant"/>'s
    /// own property doc comments for the full reasoning. No RLS change: <c>workspace</c> already
    /// carries its `tenant_isolation` policy from `AddTenantRowLevelSecurity`, and a plain column
    /// add does not touch it.
    /// </summary>
    public partial class AddWorkspaceProfileColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "country",
                table: "workspace",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "workspace",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "industry",
                table: "workspace",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "country",
                table: "workspace");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "workspace");

            migrationBuilder.DropColumn(
                name: "industry",
                table: "workspace");
        }
    }
}
