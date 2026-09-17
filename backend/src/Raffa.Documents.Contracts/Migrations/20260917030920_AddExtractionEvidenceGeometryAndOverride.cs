using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Documents.Contracts.Migrations
{
    /// <summary>
    /// Epic-23 feature-02 (NW-63r, ADR-003 w18 footer clauses 1-2). Five nullable columns on the
    /// existing <c>extraction_evidence</c> table: <c>box_x</c>/<c>box_y</c>/<c>box_width</c>/
    /// <c>box_height</c> (<c>double precision</c>, the phrase's pixel-space bounding box on the
    /// rendered page image — all four null together on every pre-existing row and any page the
    /// OCR call returned no <c>prebuilt-layout</c> geometry for) and <c>override_value</c>
    /// (<c>text</c>, the reviewer's corrected phrase, written beside <c>value</c> by the
    /// phrase-edit write path landing in feature-03 — never in place of it). No new table, so no
    /// new RLS policy — the table is already ENABLE + FORCE + tenant_isolation.
    /// </summary>
    public partial class AddExtractionEvidenceGeometryAndOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "box_height",
                table: "extraction_evidence",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "box_width",
                table: "extraction_evidence",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "box_x",
                table: "extraction_evidence",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "box_y",
                table: "extraction_evidence",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "override_value",
                table: "extraction_evidence",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "box_height",
                table: "extraction_evidence");

            migrationBuilder.DropColumn(
                name: "box_width",
                table: "extraction_evidence");

            migrationBuilder.DropColumn(
                name: "box_x",
                table: "extraction_evidence");

            migrationBuilder.DropColumn(
                name: "box_y",
                table: "extraction_evidence");

            migrationBuilder.DropColumn(
                name: "override_value",
                table: "extraction_evidence");
        }
    }
}
