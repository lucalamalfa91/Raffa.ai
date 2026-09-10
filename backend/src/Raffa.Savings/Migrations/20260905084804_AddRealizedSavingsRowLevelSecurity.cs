using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Savings.Migrations
{
    /// <summary>
    /// ADR-009 (task E04/F02/US02/T02, realized-savings): enables Postgres Row-Level Security on
    /// this module's second tenant-scoped table, <c>realized_savings</c> — the exact same mechanism
    /// <see cref="AddTenantRowLevelSecurity"/> already wired up for <c>savings_opportunity</c>, kept
    /// as its own follow-up migration rather than editing that already-shipped one (see that
    /// migration's own doc comment: "a future tenant-scoped table in this module gets its own
    /// follow-up migration adding RLS for it"). See <see cref="AddTenantRowLevelSecurity"/>'s doc
    /// comment for the full reasoning behind `FORCE`, `WITH CHECK`, and the `nullif(...)` guard —
    /// unchanged here, just re-applied to a second table.
    /// </summary>
    public partial class AddRealizedSavingsRowLevelSecurity : Migration
    {
        /// <summary>
        /// Every table backed by a <see cref="Raffa.Savings.Domain.TenantScopedEntity"/> subclass
        /// added since <see cref="AddTenantRowLevelSecurity"/> shipped (see
        /// <see cref="Infrastructure.SavingsDbContext"/>'s DbSets and this module's own
        /// `Migrations/..._AddRealizedSavings.cs`).
        /// </summary>
        private static readonly string[] TenantScopedTables =
        [
            "realized_savings",
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
