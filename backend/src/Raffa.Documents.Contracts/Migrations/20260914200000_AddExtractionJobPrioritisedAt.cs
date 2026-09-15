using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Raffa.Documents.Contracts.Infrastructure;

#nullable disable

namespace Raffa.Documents.Contracts.Migrations
{
    /// <summary>
    /// Task E16/F03/US02/T01 (priority by claim; ADR-027 w15 footer C12), built by hand on
    /// 2026-09-14 after the first real twenty-file batch on <c>dev</c>. One nullable column,
    /// <c>extraction_job.prioritised_at</c>: the instant a user opened a document that was still
    /// queued. The Worker takes a prioritised job at its next free slot in the same tenant, ahead
    /// of the FIFO, ordered by this instant.
    ///
    /// Additive and nullable, nothing dropped, no type change, no existing column becomes
    /// <c>NOT NULL</c> — the previous API and Worker images run unchanged against this schema, so
    /// a code revert needs no schema rollback (the same rule as
    /// <see cref="AddExtractionJobClaimAndDocumentRejection"/>). No new table, so no RLS statements
    /// belong here: <c>extraction_job</c> is already enabled/forced with its <c>tenant_isolation</c>
    /// policy and a column addition inherits it (ADR-009). The deployable script
    /// <c>Scripts/documents-contracts.sql</c> carries the same statement in its idempotent shape;
    /// <c>DocumentsContractsMigrationScriptTests</c> proves the two agree.
    /// </summary>
    [DbContext(typeof(DocumentsContractsDbContext))]
    [Migration("20260914200000_AddExtractionJobPrioritisedAt")]
    public partial class AddExtractionJobPrioritisedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "prioritised_at",
                table: "extraction_job",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "prioritised_at",
                table: "extraction_job");
        }
    }
}
