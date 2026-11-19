using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Raffa.Identity.Workspace.Infrastructure;

#nullable disable

namespace Raffa.Identity.Workspace.Migrations
{
    /// <summary>
    /// Marketing landing page: <c>renewal_review_request</c>, the "Free renewal review" leads an anonymous
    /// visitor submits (<c>POST /api/public/renewal-reviews</c>) and the Super Admin lists
    /// (<c>GET /api/admin/renewal-reviews</c>). A PLATFORM-level table like <c>platform_invitation</c>: it
    /// precedes any workspace, has no <c>tenant_id</c>, so no tenant RLS policy. Neither the client IP nor
    /// the user agent is stored.
    ///
    /// Raw SQL on purpose, as for the other platform tables: <c>RenewalReviewStore</c> reads and writes it
    /// through <c>Database.SqlQuery</c>/<c>ExecuteSql</c>, so the EF model snapshot is unchanged.
    /// </summary>
    [DbContext(typeof(IdentityWorkspaceDbContext))]
    [Migration("20261119200000_AddRenewalReviewRequests")]
    public partial class AddRenewalReviewRequests : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE renewal_review_request (
                    id             uuid PRIMARY KEY,
                    name           character varying(120) NOT NULL,
                    email          character varying(254) NOT NULL,
                    company        character varying(160) NOT NULL,
                    renewal_timing character varying(40) NOT NULL,
                    consent        boolean NOT NULL,
                    created_at     timestamp with time zone NOT NULL,
                    source         character varying(40) NOT NULL DEFAULT 'landing'
                );

                CREATE INDEX ix_renewal_review_request_created_at ON renewal_review_request (created_at DESC);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS renewal_review_request;");
        }
    }
}
