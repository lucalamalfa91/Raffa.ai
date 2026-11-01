using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Quotes.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteOfferDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "existing_price_label",
                table: "quote_line",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "existing_unit_price",
                table: "quote_line",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "case_id",
                table: "quote",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "checked_by",
                table: "quote",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "contract_id",
                table: "quote",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decided_at",
                table: "quote",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decided_by",
                table: "quote",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "extraction_confirmed_at",
                table: "quote",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "quote",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "missing_json",
                table: "quote",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "quote_date",
                table: "quote",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reference",
                table: "quote",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stated_terms",
                table: "quote",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "quote",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "target_json",
                table: "quote",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "term_label",
                table: "quote",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "valid_until",
                table: "quote",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_quote_tenant_id_status",
                table: "quote",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_quote_tenant_id_status",
                table: "quote");

            migrationBuilder.DropColumn(
                name: "existing_price_label",
                table: "quote_line");

            migrationBuilder.DropColumn(
                name: "existing_unit_price",
                table: "quote_line");

            migrationBuilder.DropColumn(
                name: "case_id",
                table: "quote");

            migrationBuilder.DropColumn(
                name: "checked_by",
                table: "quote");

            migrationBuilder.DropColumn(
                name: "contract_id",
                table: "quote");

            migrationBuilder.DropColumn(
                name: "decided_at",
                table: "quote");

            migrationBuilder.DropColumn(
                name: "decided_by",
                table: "quote");

            migrationBuilder.DropColumn(
                name: "extraction_confirmed_at",
                table: "quote");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "quote");

            migrationBuilder.DropColumn(
                name: "missing_json",
                table: "quote");

            migrationBuilder.DropColumn(
                name: "quote_date",
                table: "quote");

            migrationBuilder.DropColumn(
                name: "reference",
                table: "quote");

            migrationBuilder.DropColumn(
                name: "stated_terms",
                table: "quote");

            migrationBuilder.DropColumn(
                name: "status",
                table: "quote");

            migrationBuilder.DropColumn(
                name: "target_json",
                table: "quote");

            migrationBuilder.DropColumn(
                name: "term_label",
                table: "quote");

            migrationBuilder.DropColumn(
                name: "valid_until",
                table: "quote");
        }
    }
}
