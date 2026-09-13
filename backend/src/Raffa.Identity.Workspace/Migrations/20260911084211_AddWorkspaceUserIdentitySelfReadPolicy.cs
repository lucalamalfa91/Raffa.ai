using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Identity.Workspace.Migrations
{
    /// <summary>
    /// Task E14/F01/US01/T02 (story us-01-identity-scoped-rls, AC-1/AC-2): the single widening
    /// ADR-009's w14 footer and ADR-025 §F.1 describe -- one new, permissive, `FOR SELECT`-only
    /// policy, `identity_self`, on `workspace_user`. It lets a connection with no active
    /// `app.tenant_id` scope (set by <see cref="Raffa.SharedKernel.Tenancy.TenantRlsConnectionInterceptor"/>)
    /// read *only* the caller's own `workspace_user` row(s) across every tenant -- exactly the read
    /// `GET /api/workspaces` discovery needs (ADR-026 §D1 phase 1) and nothing else, because it
    /// carries no `WITH CHECK` (it can never authorize a write) and touches no other table
    /// (`workspace`, `workspace_role`, `workspace_membership` keep exactly the one
    /// `tenant_isolation` policy <c>AddTenantRowLevelSecurity</c> already gave them, unchanged).
    ///
    /// `app.identity_subject` is the new GUC task E14/F01/US01/T01 wires into that same
    /// interceptor (never edited by this task -- see this task's own "do not touch" list); this
    /// migration only adds the policy that reads it, independent of which caller sets it.
    ///
    /// The predicate is ADR-025 §F.1's own `CREATE POLICY` text verbatim (the task brief's own
    /// prose paraphrase omits the `external_subject_id` branch and the right-hand `lower(...)`;
    /// ADR-025 is the ratified, security-architect-owned source for this exact SQL and governs
    /// where the two touch one mechanism per that ADR's own header). Both guards matter:
    /// <list type="bullet">
    /// <item><c>nullif(current_setting('app.identity_subject', true), '') IS NOT NULL</c> is the
    /// fail-closed guard (AC-2): an absent *or* empty claim folds to SQL NULL, so the whole
    /// predicate is NULL (never TRUE) and the policy denies every row instead of widening by
    /// default.</item>
    /// <item>the OR-branch matching <c>external_subject_id</c> as well as <c>lower(email)</c> is
    /// what lets ADR-025 §F.2c swap `app.identity_subject`'s *source* from the interim
    /// `X-User-Id` header to the OIDC `sub`/`oid` claim in W15 (NW-05) -- which lands in
    /// `external_subject_id` via `WorkspaceMembershipService.LinkSignInAsync` -- with no policy
    /// change at all. Both sides of the email comparison are lower-cased defensively: writes are
    /// expected to already be normalised (ADR-003 w14 footer clause 5), but the predicate does not
    /// rely on that being true everywhere, forever.</item>
    /// </list>
    /// </summary>
    public partial class AddWorkspaceUserIdentitySelfReadPolicy : Migration
    {
        private const string WorkspaceUserTable = "workspace_user";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"""
                CREATE POLICY identity_self ON "{WorkspaceUserTable}"
                    FOR SELECT
                    USING (
                        nullif(current_setting('app.identity_subject', true), '') IS NOT NULL
                        AND (
                            lower(email) = lower(nullif(current_setting('app.identity_subject', true), ''))
                            OR external_subject_id = nullif(current_setting('app.identity_subject', true), '')
                        )
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"""
                DROP POLICY IF EXISTS identity_self ON "{WorkspaceUserTable}";
                """);
        }
    }
}
