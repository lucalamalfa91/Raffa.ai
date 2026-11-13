using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Renewals.Migrations
{
    /// <inheritdoc />
    /// <summary>Renewal plan F3 (mailbox): the permissions a connection granted, and the conversations Raffa
    /// started whose replies it reads (<c>case_mail_watch</c>: ids only, read by the background reader before any
    /// workspace is known, so no row-level security policy — like <c>case_mail_address</c>).</summary>
    public partial class AddMailboxReadingAndScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "scopes",
                table: "mailbox_connection",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "case_mail_watch",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_identity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    conversation_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    active_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_mail_watch", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_case_mail_watch_active_until",
                table: "case_mail_watch",
                column: "active_until");

            migrationBuilder.CreateIndex(
                name: "ix_case_mail_watch_tenant_id",
                table: "case_mail_watch",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_mail_watch_tenant_id_case_id_conversation_id",
                table: "case_mail_watch",
                columns: new[] { "tenant_id", "case_id", "conversation_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "case_mail_watch");

            migrationBuilder.DropColumn(
                name: "scopes",
                table: "mailbox_connection");
        }
    }
}
