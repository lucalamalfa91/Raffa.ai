using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Raffa.Identity.Workspace.Infrastructure;

#nullable disable

namespace Raffa.Identity.Workspace.Migrations
{
    /// <summary>
    /// Super Admin agent playground, PR 2: saved variants (the overrides a run applies on top of what production
    /// runs) and the link from a run to the variant it used and to the comparison it was part of. Platform-level,
    /// no tenant content, no RLS; raw SQL like <c>AddPlaygroundTables</c>.
    /// </summary>
    [DbContext(typeof(IdentityWorkspaceDbContext))]
    [Migration("20261119120000_AddPlaygroundVariants")]
    public partial class AddPlaygroundVariants : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE playground_variant (
                    id          uuid PRIMARY KEY,
                    name        character varying(120) NOT NULL,
                    description character varying(500) NULL,
                    overrides   jsonb NOT NULL,
                    created_by  character varying(200) NOT NULL,
                    created_at  timestamp with time zone NOT NULL,
                    updated_at  timestamp with time zone NOT NULL,
                    archived    boolean NOT NULL DEFAULT false
                );

                ALTER TABLE playground_run ADD COLUMN variant_id uuid NULL;
                ALTER TABLE playground_run ADD COLUMN variant_name character varying(120) NULL;
                ALTER TABLE playground_run ADD COLUMN comparison_id uuid NULL;
                CREATE INDEX ix_playground_run_comparison ON playground_run (comparison_id) WHERE comparison_id IS NOT NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS ix_playground_run_comparison;
                ALTER TABLE playground_run DROP COLUMN IF EXISTS comparison_id;
                ALTER TABLE playground_run DROP COLUMN IF EXISTS variant_name;
                ALTER TABLE playground_run DROP COLUMN IF EXISTS variant_id;
                DROP TABLE IF EXISTS playground_variant;
                """);
        }
    }
}
