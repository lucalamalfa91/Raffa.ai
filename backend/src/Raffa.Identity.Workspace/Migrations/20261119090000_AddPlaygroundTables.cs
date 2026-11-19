using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Raffa.Identity.Workspace.Infrastructure;

#nullable disable

namespace Raffa.Identity.Workspace.Migrations
{
    /// <summary>
    /// Super Admin agent playground (docs/architecture/admin-agent-playground-plan.md). PLATFORM-level, as
    /// <c>platform_workspace</c>: no <c>tenant_id</c> business data, so no tenant RLS policy.
    /// <c>platform_workspace.is_sandbox</c> marks the synthetic workspaces the playground runs on (they are
    /// hidden from the admin overview and workspace list and are the only tenants the playground endpoints
    /// accept); <c>playground_run</c> stores every run with its input, output and full trace.
    /// Raw SQL on purpose (see <c>AddPlatformAdminTables</c>): the EF model snapshot is unchanged.
    /// </summary>
    [DbContext(typeof(IdentityWorkspaceDbContext))]
    [Migration("20261119090000_AddPlaygroundTables")]
    public partial class AddPlaygroundTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE platform_workspace ADD COLUMN is_sandbox boolean NOT NULL DEFAULT false;

                CREATE TABLE playground_run (
                    id                 uuid PRIMARY KEY,
                    workflow           character varying(40) NOT NULL,
                    sandbox_kind       character varying(40) NOT NULL,
                    created_by         character varying(200) NOT NULL,
                    created_at         timestamp with time zone NOT NULL,
                    duration_ms        integer NOT NULL,
                    status             character varying(20) NOT NULL,
                    prompt_tokens      integer NOT NULL,
                    completion_tokens  integer NOT NULL,
                    estimated_cost_usd numeric(12,6) NULL,
                    input              jsonb NOT NULL,
                    output             jsonb NOT NULL,
                    trace              jsonb NOT NULL
                );

                CREATE INDEX ix_playground_run_created_at ON playground_run (created_at DESC);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS playground_run;
                ALTER TABLE platform_workspace DROP COLUMN IF EXISTS is_sandbox;
                """);
        }
    }
}
