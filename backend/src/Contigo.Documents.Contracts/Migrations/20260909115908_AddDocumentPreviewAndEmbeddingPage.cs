using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contigo.Documents.Contracts.Migrations
{
    /// <summary>
    /// Task E13/F04/US01/T02 (documents-v2-api): page-aware retrieval chunks and the first-page
    /// preview.
    ///
    /// <list type="bullet">
    /// <item><c>embedding.page</c> / <c>embedding.section</c> — <c>inputs/requirements.md</c>
    /// R-EVD-01 ("embedding rows store page number and section label") and R-DOC-07 AC-2 ("every
    /// new embedding row carries Page (1-based) and, when known, Section"). Both nullable: rows
    /// written before this migration, and chunks with no page concept, stay honestly unknown
    /// rather than being back-filled with a guessed 1.</item>
    /// <item><c>document.page_count</c> — R-DOC-06's list column, filled by the hybrid parse.</item>
    /// <item><c>document.preview_path</c> — R-DOC-08's tenant-prefixed preview object path
    /// (<c>DocumentStoragePath.BuildPreview</c>), never served to a client as a URL.</item>
    /// </list>
    ///
    /// No new table, so no RLS statements belong here: <c>document</c> and <c>embedding</c> are
    /// already enabled/forced with their policies by <c>AddTenantRowLevelSecurity</c>, and column
    /// additions inherit them (ADR-009; <c>Contigo.Tenancy.Tests.TenantRlsMigrationCheckTests</c>
    /// covers the invariant).
    /// </summary>
    public partial class AddDocumentPreviewAndEmbeddingPage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "page",
                table: "embedding",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "section",
                table: "embedding",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "page_count",
                table: "document",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "preview_path",
                table: "document",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "page",
                table: "embedding");

            migrationBuilder.DropColumn(
                name: "section",
                table: "embedding");

            migrationBuilder.DropColumn(
                name: "page_count",
                table: "document");

            migrationBuilder.DropColumn(
                name: "preview_path",
                table: "document");
        }
    }
}
