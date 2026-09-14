using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Documents.Contracts.Migrations
{
    /// <summary>
    /// Task E16/F02/US01/T01 (async-processing-schema, ADR-027 §D3/§D6; wave w15, NW-27 schema
    /// half): the columns the asynchronous extraction path needs. Every column is nullable or
    /// defaulted, nothing is dropped, no type changes and no existing column becomes
    /// <c>NOT NULL</c> — the previous API image runs unchanged against this schema (AC-2), so a
    /// code revert needs no schema rollback.
    ///
    /// <list type="bullet">
    /// <item><c>extraction_job.attempt_count</c> — <c>NOT NULL DEFAULT 0</c>, incremented only by
    /// the conditional-<c>UPDATE</c> claim (<see cref="Infrastructure.ExtractionJobClaimStore"/>).
    /// <c>extraction_job.claimed_at</c> / <c>claimed_by</c> — nullable; set by that same claim.
    /// Together these are AC-4's atomic claim primitive.</item>
    /// <item><c>document.rejection_reason</c> / <c>rejection_detected_type</c> /
    /// <c>rejection_confidence</c> — all nullable, set only when
    /// <c>processing_status = 'Rejected'</c>. Mirrors today's transient 422 body's
    /// <c>reason</c>/<c>detectedType</c>/<c>confidence</c>, now persisted instead of thrown away
    /// (ADR-027 §D6).</item>
    /// </list>
    ///
    /// <see cref="Domain.DocumentProcessingStatus.Rejected"/> itself needs no DDL:
    /// <c>processing_status</c> is <c>character varying(30)</c> with no CHECK constraint and no
    /// default (<c>documents-contracts.sql:110</c>, its only occurrence), so the sixth status
    /// value is a code-only change (AC-3) — this migration exists only for the three rejection
    /// detail columns above, not for the enum member itself.
    ///
    /// No new table, so no RLS statements belong here (same reasoning as
    /// <see cref="AddDocumentPreviewAndEmbeddingPage"/>): <c>document</c> and <c>extraction_job</c>
    /// are already enabled/forced with their policies, and column additions inherit them (ADR-009;
    /// <c>Raffa.Tenancy.Tests.TenantRlsMigrationCheckTests</c> covers the invariant). AC-5:
    /// <c>extraction_job</c>'s own RLS settings and <c>tenant_isolation</c> policy are untouched by
    /// this migration.
    /// </summary>
    public partial class AddExtractionJobClaimAndDocumentRejection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attempt_count",
                table: "extraction_job",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "claimed_at",
                table: "extraction_job",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "claimed_by",
                table: "extraction_job",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "rejection_confidence",
                table: "document",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejection_detected_type",
                table: "document",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                table: "document",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "attempt_count",
                table: "extraction_job");

            migrationBuilder.DropColumn(
                name: "claimed_at",
                table: "extraction_job");

            migrationBuilder.DropColumn(
                name: "claimed_by",
                table: "extraction_job");

            migrationBuilder.DropColumn(
                name: "rejection_confidence",
                table: "document");

            migrationBuilder.DropColumn(
                name: "rejection_detected_type",
                table: "document");

            migrationBuilder.DropColumn(
                name: "rejection_reason",
                table: "document");
        }
    }
}
