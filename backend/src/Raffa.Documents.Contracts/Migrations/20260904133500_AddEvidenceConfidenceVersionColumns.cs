using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Documents.Contracts.Migrations
{
    /// <inheritdoc />
    public partial class AddEvidenceConfidenceVersionColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "source_document_id",
                table: "risk",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_page",
                table: "risk",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_span",
                table: "risk",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "risk",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "source_page",
                table: "obligation",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_span",
                table: "obligation",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "obligation",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<double>(
                name: "confidence",
                table: "contract_line_item",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_document_id",
                table: "contract_line_item",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_page",
                table: "contract_line_item",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_span",
                table: "contract_line_item",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "contract_line_item",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "contract",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "clause",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "ix_risk_source_document_id",
                table: "risk",
                column: "source_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_line_item_source_document_id",
                table: "contract_line_item",
                column: "source_document_id");

            migrationBuilder.AddForeignKey(
                name: "fk_contract_line_item_document_source_document_id",
                table: "contract_line_item",
                column: "source_document_id",
                principalTable: "document",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_risk_document_source_document_id",
                table: "risk",
                column: "source_document_id",
                principalTable: "document",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_contract_line_item_document_source_document_id",
                table: "contract_line_item");

            migrationBuilder.DropForeignKey(
                name: "fk_risk_document_source_document_id",
                table: "risk");

            migrationBuilder.DropIndex(
                name: "ix_risk_source_document_id",
                table: "risk");

            migrationBuilder.DropIndex(
                name: "ix_contract_line_item_source_document_id",
                table: "contract_line_item");

            migrationBuilder.DropColumn(
                name: "source_document_id",
                table: "risk");

            migrationBuilder.DropColumn(
                name: "source_page",
                table: "risk");

            migrationBuilder.DropColumn(
                name: "source_span",
                table: "risk");

            migrationBuilder.DropColumn(
                name: "version",
                table: "risk");

            migrationBuilder.DropColumn(
                name: "source_page",
                table: "obligation");

            migrationBuilder.DropColumn(
                name: "source_span",
                table: "obligation");

            migrationBuilder.DropColumn(
                name: "version",
                table: "obligation");

            migrationBuilder.DropColumn(
                name: "confidence",
                table: "contract_line_item");

            migrationBuilder.DropColumn(
                name: "source_document_id",
                table: "contract_line_item");

            migrationBuilder.DropColumn(
                name: "source_page",
                table: "contract_line_item");

            migrationBuilder.DropColumn(
                name: "source_span",
                table: "contract_line_item");

            migrationBuilder.DropColumn(
                name: "version",
                table: "contract_line_item");

            migrationBuilder.DropColumn(
                name: "version",
                table: "contract");

            migrationBuilder.DropColumn(
                name: "version",
                table: "clause");
        }
    }
}
