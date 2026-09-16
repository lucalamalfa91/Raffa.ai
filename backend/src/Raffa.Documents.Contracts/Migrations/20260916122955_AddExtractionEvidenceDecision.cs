using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Documents.Contracts.Migrations
{
    /// <summary>
    /// Task E22/F01/US01/T01 (auto-accept decision). Two nullable columns on the existing
    /// <c>extraction_evidence</c> table: <c>decision</c> (<c>character varying</c>, one of
    /// auto_accepted / human_accepted / review_required) and <c>decided_at</c>
    /// (<c>timestamp with time zone</c>). No new table, so no new RLS policy — the table is
    /// already ENABLE + FORCE + tenant_isolation.
    /// </summary>
    public partial class AddExtractionEvidenceDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decided_at",
                table: "extraction_evidence",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decision",
                table: "extraction_evidence",
                type: "character varying",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "decided_at",
                table: "extraction_evidence");

            migrationBuilder.DropColumn(
                name: "decision",
                table: "extraction_evidence");
        }
    }
}
