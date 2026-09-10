using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Quotes.Migrations
{
    /// <summary>
    /// ADR-009 (task E05/F01/US01/T01, quote-extraction): enables Postgres Row-Level Security on
    /// every tenant-scoped table in this bounded context and adds a policy keyed to the
    /// per-connection `current_setting('app.tenant_id', true)` claim that
    /// <see cref="Raffa.SharedKernel.Tenancy.TenantRlsConnectionInterceptor"/> sets — the same
    /// mechanism every other module's own `AddTenantRowLevelSecurity` migration already wires up
    /// (most recently <c>Raffa.Savings.Migrations.AddTenantRowLevelSecurity</c>), applied here to
    /// this module's own tables. `FORCE` makes the policy apply even to the table owner (the role
    /// migrations/the app run as), not just to ordinary non-owner roles. `WITH CHECK` mirrors
    /// `USING` so a session cannot write a row into a tenant it is not scoped to, not just read
    /// across tenants. `nullif(..., '')` guards the `::uuid` cast: `current_setting(..., true)`
    /// returns NULL when the GUC was never set on this session, but Postgres can also leave a
    /// *pooled* connection's custom GUC as an empty string after `RESET app.tenant_id` rather than
    /// fully undefined — casting `''::uuid` throws, which would turn "no active tenant scope" into
    /// a 500 instead of the intended zero-rows-visible. `nullif` folds both "never set" and "reset
    /// to empty" to SQL NULL first, so the row comparison always safely evaluates to NULL (never
    /// TRUE) instead of erroring.
    ///
    /// The table list is intentionally the fixed set that exists as of this migration (mirrors how
    /// `Initial` hardcodes its own tables) — a future tenant-scoped table in this module gets its
    /// own follow-up migration adding RLS for it. What stays generic and catches an omission is the
    /// CI migration check (`Raffa.Quotes.Tests.QuoteRlsMigrationCheckTests`), which discovers
    /// tenant-scoped tables dynamically from the EF model (every
    /// <see cref="Raffa.Quotes.Domain.TenantScopedEntity"/> subclass) rather than from this
    /// hardcoded list.
    /// </summary>
    public partial class AddTenantRowLevelSecurity : Migration
    {
        /// <summary>
        /// Every table backed by a <see cref="Raffa.Quotes.Domain.TenantScopedEntity"/> subclass
        /// as of this migration (see <see cref="Infrastructure.QuotesDbContext"/>'s DbSets and
        /// `Migrations/..._Initial.cs`).
        /// </summary>
        private static readonly string[] TenantScopedTables =
        [
            "quote",
            "quote_extraction_job",
            "quote_line",
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
