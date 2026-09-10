using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Quotes.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "quote",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    storage_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    checksum = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    processing_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quote", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quote_extraction_job",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    quote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    model_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quote_extraction_job", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quote_line",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    quote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    edition = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    list_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    discount_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    term = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    extended_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    source_span = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    source_page = table.Column<int>(type: "integer", nullable: true),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quote_line", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_quote_tenant_id",
                table: "quote",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_quote_extraction_job_tenant_id",
                table: "quote_extraction_job",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_quote_extraction_job_tenant_id_quote_id",
                table: "quote_extraction_job",
                columns: new[] { "tenant_id", "quote_id" });

            migrationBuilder.CreateIndex(
                name: "ix_quote_line_tenant_id",
                table: "quote_line",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_quote_line_tenant_id_quote_id",
                table: "quote_line",
                columns: new[] { "tenant_id", "quote_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quote");

            migrationBuilder.DropTable(
                name: "quote_extraction_job");

            migrationBuilder.DropTable(
                name: "quote_line");
        }
    }
}
