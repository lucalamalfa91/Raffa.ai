using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Raffa.Identity.Workspace.Infrastructure;

#nullable disable

namespace Raffa.Identity.Workspace.Migrations
{
    /// <summary>
    /// Portal admin (Raffa staff). Two PLATFORM-level tables, deliberately outside the tenant model:
    /// <c>platform_workspace</c> (a metadata registry: id, name, admin email, created, status -- no
    /// business data) and <c>platform_invitation</c> (prospect invitations that precede any workspace).
    /// Neither carries <c>tenant_id</c> business data, so neither gets a tenant RLS policy; RLS cannot
    /// express "list every tenant", and the product forbids <c>BYPASSRLS</c> (ADR-009), so the
    /// registry is the sanctioned way to list workspaces without ever reading tenant rows.
    ///
    /// Raw SQL on purpose: the tables are read and written by <c>PlatformAdminService</c> through
    /// <c>Database.SqlQuery</c>/<c>ExecuteSql</c>, not mapped entities, so the EF model snapshot is
    /// unchanged and cannot drift. Existing workspaces register themselves lazily the first time a
    /// member lists their workspaces (nothing here reads the RLS-forced <c>workspace</c> table).
    /// </summary>
    [DbContext(typeof(IdentityWorkspaceDbContext))]
    [Migration("20261102100000_AddPlatformAdminTables")]
    public partial class AddPlatformAdminTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE platform_workspace (
                    tenant_id   uuid PRIMARY KEY,
                    name        character varying(200) NOT NULL,
                    admin_email character varying(320) NULL,
                    created_at  timestamp with time zone NOT NULL,
                    status      character varying(20) NOT NULL DEFAULT 'active',
                    CONSTRAINT ck_platform_workspace_status CHECK (status IN ('active', 'suspended'))
                );

                CREATE TABLE platform_invitation (
                    id                  uuid PRIMARY KEY,
                    email               character varying(320) NOT NULL,
                    token_hash          character varying(128) NOT NULL,
                    created_by          character varying(200) NOT NULL,
                    created_at          timestamp with time zone NOT NULL,
                    expires_at          timestamp with time zone NOT NULL,
                    accepted_at         timestamp with time zone NULL,
                    accepted_by_subject character varying(200) NULL,
                    workspace_id        uuid NULL,
                    revoked_at          timestamp with time zone NULL
                );

                CREATE UNIQUE INDEX ux_platform_invitation_token_hash ON platform_invitation (token_hash);
                -- At most one live (not accepted, not revoked) invitation per address.
                CREATE UNIQUE INDEX ux_platform_invitation_live_email ON platform_invitation (lower(email))
                    WHERE accepted_at IS NULL AND revoked_at IS NULL;
                CREATE INDEX ix_platform_invitation_accepted_by ON platform_invitation (accepted_by_subject)
                    WHERE accepted_by_subject IS NOT NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS platform_invitation;
                DROP TABLE IF EXISTS platform_workspace;
                """);
        }
    }
}
