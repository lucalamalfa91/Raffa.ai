using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Documents.Contracts.Migrations
{
    /// <summary>
    /// Instant-identity-ingest slice: three nullable columns on <c>contract</c> that support
    /// the provisional/official identity lifecycle.
    ///
    /// <list type="bullet">
    /// <item><c>display_name</c>: the filename at upload time, optionally overwritten by the
    /// headline LLM if it finds a better title. Nullable so pre-existing rows are not data-migrated.</item>
    /// <item><c>identity_state</c>: <c>'provisional'</c> after upload + headline pass,
    /// <c>'official'</c> after full 7-stage enrich. <c>DEFAULT 'official'</c> so every
    /// pre-existing, already-extracted row reads as official without a data migration.</item>
    /// <item><c>provisional_supplier_name</c>: raw supplier string from the headline pass,
    /// always persisted regardless of confidence. Nullable so pre-existing rows are unaffected.</item>
    /// </list>
    ///
    /// Additive-only: no column is dropped, renamed, or made NOT NULL without a default.
    /// The previous API and Worker images run unchanged against this schema (the three new columns
    /// are all nullable or have a server default). No new table, so no additional RLS statements
    /// are needed — the <c>contract</c> table already has <c>ENABLE / FORCE ROW LEVEL SECURITY</c>
    /// and a <c>tenant_isolation</c> policy (added by <c>AddTenantRowLevelSecurity</c>), and a
    /// column addition inherits them (ADR-009).
    /// </summary>
    public partial class AddContractProvisionalIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "display_name",
                table: "contract",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "identity_state",
                table: "contract",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValueSql: "'official'");

            migrationBuilder.AddColumn<string>(
                name: "provisional_supplier_name",
                table: "contract",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "display_name",
                table: "contract");

            migrationBuilder.DropColumn(
                name: "identity_state",
                table: "contract");

            migrationBuilder.DropColumn(
                name: "provisional_supplier_name",
                table: "contract");
        }
    }
}
