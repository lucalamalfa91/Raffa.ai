using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Renewals.Migrations
{
    /// <inheritdoc />
    /// <summary>Renewal plan F2/F3: connected company mailboxes, case addresses, inbound supplier mail,
    /// notifications and notification channels; <c>case_email.sent_via</c>.</summary>
    public partial class AddMailOutreachAndNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "sent_via",
                table: "case_email",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "case_inbound_mail",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider_message_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    from_address = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    preview = table.Column<string>(type: "text", nullable: false),
                    attachment_names_json = table.Column<string>(type: "jsonb", nullable: true),
                    raw_storage_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decided_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reply_index = table.Column<int>(type: "integer", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_inbound_mail", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "case_mail_address",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_mail_address", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mailbox_connection",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_identity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    refresh_token_protected = table.Column<string>(type: "text", nullable: false),
                    connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mailbox_connection", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_identity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    link = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    case_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vars_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_channel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_identity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_protected = table.Column<string>(type: "text", nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_channel", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_case_inbound_mail_tenant_id",
                table: "case_inbound_mail",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_inbound_mail_tenant_id_case_id",
                table: "case_inbound_mail",
                columns: new[] { "tenant_id", "case_id" });

            migrationBuilder.CreateIndex(
                name: "ix_case_inbound_mail_tenant_id_provider_provider_message_id",
                table: "case_inbound_mail",
                columns: new[] { "tenant_id", "provider", "provider_message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_case_mail_address_tenant_id",
                table: "case_mail_address",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_mail_address_tenant_id_case_id",
                table: "case_mail_address",
                columns: new[] { "tenant_id", "case_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_case_mail_address_token",
                table: "case_mail_address",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mailbox_connection_tenant_id",
                table: "mailbox_connection",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_mailbox_connection_tenant_id_user_identity",
                table: "mailbox_connection",
                columns: new[] { "tenant_id", "user_identity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_tenant_id",
                table: "notification",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_tenant_id_user_identity_created_at",
                table: "notification",
                columns: new[] { "tenant_id", "user_identity", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_channel_tenant_id",
                table: "notification_channel",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_channel_tenant_id_user_identity",
                table: "notification_channel",
                columns: new[] { "tenant_id", "user_identity" });

            // Row-Level Security like every Renewals table. case_mail_address deliberately has none: it only
            // routes an incoming message to its workspace and case (no customer content) and is read by the
            // inbound webhook before any workspace is known.
            migrationBuilder.Sql(
                """
                ALTER TABLE "mailbox_connection" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "mailbox_connection" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "mailbox_connection"
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE "case_inbound_mail" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "case_inbound_mail" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "case_inbound_mail"
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE "notification" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "notification" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "notification"
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE "notification_channel" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "notification_channel" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "notification_channel"
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation ON "mailbox_connection";
                ALTER TABLE "mailbox_connection" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "mailbox_connection" DISABLE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON "case_inbound_mail";
                ALTER TABLE "case_inbound_mail" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "case_inbound_mail" DISABLE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON "notification";
                ALTER TABLE "notification" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "notification" DISABLE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON "notification_channel";
                ALTER TABLE "notification_channel" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "notification_channel" DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "case_inbound_mail");

            migrationBuilder.DropTable(
                name: "case_mail_address");

            migrationBuilder.DropTable(
                name: "mailbox_connection");

            migrationBuilder.DropTable(
                name: "notification");

            migrationBuilder.DropTable(
                name: "notification_channel");

            migrationBuilder.DropColumn(
                name: "sent_via",
                table: "case_email");
        }
    }
}
