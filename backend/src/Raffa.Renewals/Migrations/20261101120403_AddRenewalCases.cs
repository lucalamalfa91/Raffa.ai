using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Renewals.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// AskRaffa 4.0 (slices 2+3, BACKEND-SPEC §2.1-2.7/2.12): the renewal case aggregate
    /// (<c>renewal_case</c>, state jsonb + version, one open case per contract) and its projections
    /// (<c>case_email</c>, <c>case_delegation</c>, append-only <c>case_event</c>,
    /// <c>case_vendor_message</c> with hash dedup, <c>user_reminder</c>, <c>today_snooze</c>). Row-Level
    /// Security on every new table, same SQL as <see cref="AddRenewalNegotiationTodo"/>.
    /// <c>renewal_action</c> stays (read by the pre-4.0 surfaces) but 4.0 no longer writes it.
    /// </summary>
    public partial class AddRenewalCases : Migration
    {
        private static readonly string[] CaseTables =
            ["renewal_case", "case_email", "case_delegation", "case_event", "case_vendor_message", "user_reminder", "today_snooze"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "case_delegation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    can_approve = table.Column<bool>(type: "boolean", nullable: false),
                    can_close = table.Column<bool>(type: "boolean", nullable: false),
                    granted_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_delegation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "case_email",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email_index = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    to_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    subject = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    attachments_json = table.Column<string>(type: "jsonb", nullable: true),
                    approved_version = table.Column<int>(type: "integer", nullable: true),
                    approved_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    copied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_body = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_email", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "case_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: true),
                    email_index = table.Column<int>(type: "integer", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_event", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "case_vendor_message",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    pasted_text = table.Column<string>(type: "text", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    thread_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    parsed_json = table.Column<string>(type: "jsonb", nullable: true),
                    analysed_by_model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    prompt_version = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    input_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_vendor_message", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "renewal_case",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approver_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    phase = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    step = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    branch = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    state = table.Column<string>(type: "jsonb", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    notice_deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    contract_end = table.Column<DateOnly>(type: "date", nullable: true),
                    outcome_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    outcome_json = table.Column<string>(type: "jsonb", nullable: true),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_renewal_case", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "today_snooze",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    until = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_today_snooze", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_reminder",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reminder_index = table.Column<int>(type: "integer", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_reminder", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_case_delegation_tenant_id",
                table: "case_delegation",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_delegation_tenant_id_case_id",
                table: "case_delegation",
                columns: new[] { "tenant_id", "case_id" });

            migrationBuilder.CreateIndex(
                name: "ix_case_email_tenant_id",
                table: "case_email",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_email_tenant_id_case_id_email_index",
                table: "case_email",
                columns: new[] { "tenant_id", "case_id", "email_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_case_event_tenant_id",
                table: "case_event",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_event_tenant_id_case_id_at",
                table: "case_event",
                columns: new[] { "tenant_id", "case_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_case_vendor_message_tenant_id",
                table: "case_vendor_message",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_vendor_message_tenant_id_case_id_content_hash",
                table: "case_vendor_message",
                columns: new[] { "tenant_id", "case_id", "content_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_renewal_case_tenant_id",
                table: "renewal_case",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_renewal_case_tenant_contract_open",
                table: "renewal_case",
                columns: new[] { "tenant_id", "contract_id" },
                unique: true,
                filter: "closed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_today_snooze_tenant_id",
                table: "today_snooze",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_today_snooze_tenant_id_user_id_item_key",
                table: "today_snooze",
                columns: new[] { "tenant_id", "user_id", "item_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_reminder_tenant_id",
                table: "user_reminder",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_reminder_tenant_id_user_id_due_date",
                table: "user_reminder",
                columns: new[] { "tenant_id", "user_id", "due_date" });
            foreach (var table in CaseTables)
            {
                migrationBuilder.Sql(
                    $"""
                    ALTER TABLE "{table}" ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE "{table}" FORCE ROW LEVEL SECURITY;
                    CREATE POLICY tenant_isolation ON "{table}"
                        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                    """);
            }
        }


        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in CaseTables)
            {
                migrationBuilder.Sql(
                    $"""
                    DROP POLICY IF EXISTS tenant_isolation ON "{table}";
                    ALTER TABLE "{table}" NO FORCE ROW LEVEL SECURITY;
                    ALTER TABLE "{table}" DISABLE ROW LEVEL SECURITY;
                    """);
            }

            migrationBuilder.DropTable(
                name: "case_delegation");

            migrationBuilder.DropTable(
                name: "case_email");

            migrationBuilder.DropTable(
                name: "case_event");

            migrationBuilder.DropTable(
                name: "case_vendor_message");

            migrationBuilder.DropTable(
                name: "renewal_case");

            migrationBuilder.DropTable(
                name: "today_snooze");

            migrationBuilder.DropTable(
                name: "user_reminder");
        }
    }
}
