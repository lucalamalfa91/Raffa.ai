using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Identity.Workspace.Migrations
{
    /// <summary>
    /// Task E14/F01/US01/T02 (story us-01-identity-scoped-rls AC-4; NW-58; ADR-025 §D.1/§F.4;
    /// ADR-026 §D4; ADR-003 w14 amendment footer clause 2): creates this module's third
    /// tenant-scoped table, <c>workspace_invitation</c> (see
    /// <see cref="Raffa.Identity.Workspace.Domain.WorkspaceInvitation"/>'s own doc comment), and
    /// enables/forces Postgres Row-Level Security and the same <c>tenant_isolation</c> policy
    /// shape <c>AddTenantRowLevelSecurity</c> gave every table that existed as of that migration
    /// -- combined here, in the same migration, exactly like
    /// <c>Raffa.Documents.Contracts.Migrations.AddContractLineItem</c> and
    /// <c>Raffa.Renewals.Migrations.AddRenewalAlert</c> already did for a brand-new tenant table:
    /// the table does not exist before this migration runs, so there is no migration history state
    /// where it exists without RLS. It gets **no** <c>identity_self</c> policy -- that widening is
    /// confined to <c>workspace_user</c> (`AddWorkspaceUserIdentitySelfReadPolicy`).
    ///
    /// Also hand-adds the one index EF's fluent model cannot express: the partial unique
    /// expression index <c>(tenant_id, lower(email)) WHERE accepted_at IS NULL AND revoked_at IS
    /// NULL</c> (ADR-025 §F.4, ADR-026 §D4) that keeps at most one *live* invitation per address
    /// per tenant, so re-invites cannot accumulate valid links. See
    /// <see cref="Raffa.Identity.Workspace.Infrastructure.Configurations.WorkspaceInvitationConfiguration"/>'s
    /// own doc comment for why this lives here as raw SQL instead of fluent configuration.
    /// </summary>
    public partial class AddWorkspaceInvitation : Migration
    {
        private const string WorkspaceInvitationTable = "workspace_invitation";

        private const string LiveInvitationPerAddressIndex = "ix_workspace_invitation_tenant_id_email";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workspace_invitation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    workspace_role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    invited_by = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspace_invitation", x => x.id);
                    table.ForeignKey(
                        name: "fk_workspace_invitation_workspace_role_workspace_role_id",
                        column: x => x.workspace_role_id,
                        principalTable: "workspace_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_workspace_invitation_tenant_id",
                table: "workspace_invitation",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_workspace_invitation_tenant_id_token_hash",
                table: "workspace_invitation",
                columns: new[] { "tenant_id", "token_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workspace_invitation_workspace_role_id",
                table: "workspace_invitation",
                column: "workspace_role_id");

            // ADR-025 §F.4 / ADR-026 §D4: at most one *live* invitation per (tenant, address) --
            // "live" meaning neither accepted nor revoked -- so a re-invite of a still-pending
            // address cannot accumulate a second valid link. lower(email) mirrors the
            // identity_self predicate's own defensive lower-casing (see
            // AddWorkspaceUserIdentitySelfReadPolicy's doc comment); an expired-but-otherwise-live
            // row still holds the slot deliberately -- a partial index predicate must be
            // immutable, and "expired" is a clock comparison, not a stored fact (ADR-020 w14
            // second footer). Not expressible via EF's fluent HasIndex (see
            // WorkspaceInvitationConfiguration's own doc comment), hence raw SQL.
            migrationBuilder.Sql(
                $"""
                CREATE UNIQUE INDEX {LiveInvitationPerAddressIndex} ON "{WorkspaceInvitationTable}" (tenant_id, lower(email))
                    WHERE accepted_at IS NULL AND revoked_at IS NULL;
                """);

            // ADR-009 / ADR-025 §F.4: this table is tenant-scoped (TenantScopedEntity) and must
            // never ship without RLS -- same ENABLE / FORCE / CREATE POLICY SQL and
            // nullif(current_setting(...), '')::uuid NULL-safety guard as
            // AddTenantRowLevelSecurity, applied to this one new table. No identity_self policy:
            // that widening is confined to workspace_user.
            migrationBuilder.Sql(
                $"""
                ALTER TABLE "{WorkspaceInvitationTable}" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "{WorkspaceInvitationTable}" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "{WorkspaceInvitationTable}"
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"""
                DROP POLICY IF EXISTS tenant_isolation ON "{WorkspaceInvitationTable}";
                ALTER TABLE "{WorkspaceInvitationTable}" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "{WorkspaceInvitationTable}" DISABLE ROW LEVEL SECURITY;
                DROP INDEX IF EXISTS {LiveInvitationPerAddressIndex};
                """);

            migrationBuilder.DropTable(
                name: "workspace_invitation");
        }
    }
}
