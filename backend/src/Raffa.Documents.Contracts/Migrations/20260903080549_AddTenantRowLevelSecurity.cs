using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Documents.Contracts.Migrations
{
    /// <summary>
    /// ADR-009 (AC-2/AC-3): enables Postgres Row-Level Security on every tenant-scoped table in
    /// this bounded context and adds a policy keyed to the per-connection
    /// `current_setting('app.tenant_id', true)` claim that
    /// <see cref="Raffa.SharedKernel.Tenancy.TenantRlsConnectionInterceptor"/> sets. `FORCE`
    /// makes the policy apply even to the table owner (the role migrations/the app run as),
    /// not just to ordinary non-owner roles — RLS is a backstop against a missing/wrong
    /// application-level `WHERE tenant_id = ...`, so it must not be silently skipped for the
    /// app's own role. `WITH CHECK` mirrors `USING` so a session cannot write a row into a
    /// tenant it is not scoped to, not just read across tenants. `nullif(..., '')` guards the
    /// `::uuid` cast: `current_setting(..., true)` returns NULL when the GUC was never set on
    /// this session, but Postgres can also leave a *pooled* connection's custom GUC as an empty
    /// string after `RESET app.tenant_id`
    /// (<see cref="Raffa.SharedKernel.Tenancy.TenantRlsConnectionInterceptor"/> resets on
    /// connection return) rather than fully undefined — casting `''::uuid` throws, which would
    /// turn "no active tenant scope" into a 500 instead of the intended zero-rows-visible.
    /// `nullif` folds both "never set" and "reset to empty" to SQL NULL first, so the row
    /// comparison always safely evaluates to NULL (never TRUE) instead of erroring.
    ///
    /// The table list is intentionally the fixed set that exists as of this migration (mirrors
    /// how `Initial` hardcodes its own tables) — a future tenant-scoped table gets its own
    /// follow-up migration adding RLS for it. What stays generic and catches an omission is the
    /// CI migration check (`tests/Raffa.Tenancy`), which discovers tenant-scoped tables
    /// dynamically from the EF model (every <see cref="Raffa.Documents.Contracts.Domain.TenantScopedEntity"/>
    /// subclass) rather than from this hardcoded list.
    /// </summary>
    public partial class AddTenantRowLevelSecurity : Migration
    {
        /// <summary>
        /// Every table backed by a <see cref="Raffa.Documents.Contracts.Domain.TenantScopedEntity"/>
        /// subclass as of this migration (see <see cref="DocumentsContractsDbContext"/>'s
        /// DbSets and `Migrations/20260902205243_Initial.cs`).
        /// </summary>
        private static readonly string[] TenantScopedTables =
        [
            "contract",
            "contract_version",
            "correction_history",
            "document",
            "document_version",
            "extraction_job",
            "clause",
            "obligation",
            "risk",
            "embedding",
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
