using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Chat.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationArchiveAndCase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "title",
                table: "conversation",
                type: "character varying(96)",
                maxLength: 96,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(48)",
                oldMaxLength: 48);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "archived_at",
                table: "conversation",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "case_id",
                table: "conversation",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversation_tenant_id_case_id",
                table: "conversation",
                columns: new[] { "tenant_id", "case_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_conversation_tenant_id_case_id",
                table: "conversation");

            migrationBuilder.DropColumn(
                name: "archived_at",
                table: "conversation");

            migrationBuilder.DropColumn(
                name: "case_id",
                table: "conversation");

            migrationBuilder.AlterColumn<string>(
                name: "title",
                table: "conversation",
                type: "character varying(48)",
                maxLength: 48,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(96)",
                oldMaxLength: 96);
        }
    }
}
