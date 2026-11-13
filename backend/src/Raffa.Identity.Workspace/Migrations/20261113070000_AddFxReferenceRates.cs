using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Raffa.Identity.Workspace.Infrastructure;

#nullable disable

namespace Raffa.Identity.Workspace.Migrations
{
    /// <summary>
    /// Spesa e risparmi: <c>fx_reference_rate</c>, the latest official reference rates (European Central
    /// Bank, units of each currency per one euro) downloaded every morning by the API host. A PLATFORM-level
    /// table like <c>platform_workspace</c>: public market data shared by every workspace, no
    /// <c>tenant_id</c>, so no tenant RLS policy. One row per currency, overwritten by each refresh.
    ///
    /// Raw SQL on purpose, as for the platform tables: <c>FxReferenceRateStore</c> reads and writes it through
    /// <c>Database.SqlQuery</c>/<c>ExecuteSql</c>, so the EF model snapshot is unchanged.
    /// </summary>
    [DbContext(typeof(IdentityWorkspaceDbContext))]
    [Migration("20261113070000_AddFxReferenceRates")]
    public partial class AddFxReferenceRates : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE fx_reference_rate (
                    currency     character varying(3) PRIMARY KEY,
                    per_eur      numeric(20,8) NOT NULL,
                    rate_date    date NOT NULL,
                    source       character varying(20) NOT NULL,
                    fetched_at   timestamp with time zone NOT NULL,
                    CONSTRAINT ck_fx_reference_rate_positive CHECK (per_eur > 0)
                );
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS fx_reference_rate;");
        }
    }
}
