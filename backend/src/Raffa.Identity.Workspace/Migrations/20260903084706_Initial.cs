using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Identity.Workspace.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workspace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspace", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workspace_role",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspace_role", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workspace_user",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    external_subject_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspace_user", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workspace_membership",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspace_membership", x => x.id);
                    table.ForeignKey(
                        name: "fk_workspace_membership_workspace_role_workspace_role_id",
                        column: x => x.workspace_role_id,
                        principalTable: "workspace_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workspace_membership_workspace_user_workspace_user_id",
                        column: x => x.workspace_user_id,
                        principalTable: "workspace_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_workspace_tenant_id",
                table: "workspace",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workspace_membership_tenant_id",
                table: "workspace_membership",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_workspace_membership_workspace_role_id",
                table: "workspace_membership",
                column: "workspace_role_id");

            migrationBuilder.CreateIndex(
                name: "ix_workspace_membership_workspace_user_id_workspace_role_id",
                table: "workspace_membership",
                columns: new[] { "workspace_user_id", "workspace_role_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workspace_role_tenant_id",
                table: "workspace_role",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_workspace_role_tenant_id_name",
                table: "workspace_role",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workspace_user_tenant_id",
                table: "workspace_user",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_workspace_user_tenant_id_email",
                table: "workspace_user",
                columns: new[] { "tenant_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workspace_user_tenant_id_external_subject_id",
                table: "workspace_user",
                columns: new[] { "tenant_id", "external_subject_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workspace");

            migrationBuilder.DropTable(
                name: "workspace_membership");

            migrationBuilder.DropTable(
                name: "workspace_role");

            migrationBuilder.DropTable(
                name: "workspace_user");
        }
    }
}
