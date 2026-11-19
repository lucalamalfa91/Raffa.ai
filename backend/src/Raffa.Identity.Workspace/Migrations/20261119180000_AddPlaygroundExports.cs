using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Raffa.Identity.Workspace.Infrastructure;

#nullable disable

namespace Raffa.Identity.Workspace.Migrations
{
    /// <summary>
    /// Super Admin agent playground, PR 5: the record of every export of a variant to the repository (a draft pull request
    /// or a downloaded bundle): which variant, which branch and pull request, what was written, who asked, and why it failed
    /// when it did. Platform-level, no tenant content, no RLS; raw SQL like the other playground tables.
    /// </summary>
    [DbContext(typeof(IdentityWorkspaceDbContext))]
    [Migration("20261119180000_AddPlaygroundExports")]
    public partial class AddPlaygroundExports : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE playground_export (
                    id           uuid PRIMARY KEY,
                    variant_id   uuid NOT NULL,
                    variant_name character varying(120) NOT NULL,
                    target       character varying(20) NOT NULL,
                    status       character varying(20) NOT NULL,
                    branch       character varying(200) NULL,
                    pr_number    integer NULL,
                    pr_url       character varying(500) NULL,
                    error        character varying(1000) NULL,
                    files        jsonb NOT NULL,
                    created_by   character varying(200) NOT NULL,
                    created_at   timestamp with time zone NOT NULL
                );

                CREATE INDEX ix_playground_export_variant ON playground_export (variant_id, created_at DESC);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS playground_export;");
        }
    }
}
