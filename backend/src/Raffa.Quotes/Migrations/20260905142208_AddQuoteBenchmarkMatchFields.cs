using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Quotes.Migrations
{
    /// <summary>
    /// Task E05/F02/US01/T01 (market-assessment): adds the four Quote-level benchmark-matching
    /// columns (<c>supplier</c>, <c>currency</c>, <c>geography</c>, <c>purchase_date</c>) to the
    /// existing <c>quote</c> table — see <see cref="Raffa.Quotes.Domain.Quote"/>'s own doc comment
    /// for why they are nullable and caller-supplied. No RLS change needed: <c>quote</c> already has
    /// row-level security from the initial migration (<c>AddTenantRowLevelSecurity</c>), which is a
    /// table/row-level policy that automatically covers new columns on the same table — unlike
    /// <c>AddSkuProductMapping</c>, this migration adds no new table.
    /// </summary>
    public partial class AddQuoteBenchmarkMatchFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "quote",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "geography",
                table: "quote",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "purchase_date",
                table: "quote",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "supplier",
                table: "quote",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "currency",
                table: "quote");

            migrationBuilder.DropColumn(
                name: "geography",
                table: "quote");

            migrationBuilder.DropColumn(
                name: "purchase_date",
                table: "quote");

            migrationBuilder.DropColumn(
                name: "supplier",
                table: "quote");
        }
    }
}
