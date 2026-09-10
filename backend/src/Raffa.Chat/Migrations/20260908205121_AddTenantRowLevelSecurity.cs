using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Chat.Migrations
{
    /// <summary>
    /// ADR-009 (AC-2/AC-3), task E13/F05/US01/T01: enables Postgres Row-Level Security on every
    /// tenant-scoped table in this bounded context and adds a policy keyed to the per-connection
    /// `current_setting('app.tenant_id', true)` claim that
    /// <see cref="Raffa.SharedKernel.Tenancy.TenantRlsConnectionInterceptor"/> sets — the exact
    /// same SQL shape as
    /// <c>Raffa.Documents.Contracts.Migrations.AddTenantRowLevelSecurity</c> (see that
    /// migration's own doc comment for the `FORCE`/`WITH CHECK`/`nullif` rationale, unchanged
    /// here). `WITH CHECK` mirrors `USING` so a session cannot write a row into a tenant it is
    /// not scoped to, not just read across tenants.
    ///
    /// The table list is intentionally the fixed set that exists as of this migration (mirrors
    /// how `Initial` hardcodes its own tables) — a future tenant-scoped table added to this
    /// module gets its own follow-up migration adding RLS for it.
    /// </summary>
    public partial class AddTenantRowLevelSecurity : Migration
    {
        /// <summary>
        /// Every table backed by a <see cref="Raffa.Chat.Domain.Conversations.TenantScopedEntity"/>
        /// subclass as of this migration (see <see cref="Infrastructure.ChatDbContext"/>'s DbSets
        /// and `Migrations/20260908205020_Initial.cs`).
        /// </summary>
        private static readonly string[] TenantScopedTables =
        [
            "conversation",
            "conversation_message",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantScopedTables)
            {
                migrationBuilder.Sql(
                    $"""
                    ALTER TABLE "{table}" ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE "{table}" FORCE ROW LEVEL SECURITY;
                    CREATE POLICY tenant_isolation ON "{table}"
                        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantScopedTables)
            {
                migrationBuilder.Sql(
                    $"""
                    DROP POLICY IF EXISTS tenant_isolation ON "{table}";
                    ALTER TABLE "{table}" NO FORCE ROW LEVEL SECURITY;
                    ALTER TABLE "{table}" DISABLE ROW LEVEL SECURITY;
                    """);
            }
        }
    }
}
