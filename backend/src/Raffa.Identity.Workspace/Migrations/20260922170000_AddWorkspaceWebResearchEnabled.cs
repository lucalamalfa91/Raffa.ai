using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Identity.Workspace.Migrations
{
    /// <summary>
    /// ADR-030 (Ask Raffa web research, gate 2): <c>workspace.web_research_enabled boolean NOT NULL
    /// DEFAULT false</c> -- the workspace Admin's opt-in. A closed switch is a safe default for a
    /// populated table (unlike the profile columns, which stay nullable because "unknown" is not a
    /// value). No RLS change: <c>workspace</c> already carries its <c>tenant_isolation</c> policy.
    /// </summary>
    public partial class AddWorkspaceWebResearchEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "web_research_enabled",
                table: "workspace",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "web_research_enabled",
                table: "workspace");
        }
    }
}
