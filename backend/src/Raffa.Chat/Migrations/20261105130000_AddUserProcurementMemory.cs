using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Raffa.Chat.Infrastructure;

#nullable disable

namespace Raffa.Chat.Migrations;

[DbContext(typeof(ChatDbContext))]
[Migration("20261105130000_AddUserProcurementMemory")]
public partial class AddUserProcurementMemory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "user_procurement_memory",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                profile_json = table.Column<string>(type: "jsonb", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_user_procurement_memory", x => x.id));
        migrationBuilder.CreateIndex(name: "ix_user_procurement_memory_expires_at", table: "user_procurement_memory", column: "expires_at");
        migrationBuilder.CreateIndex(name: "ix_user_procurement_memory_tenant_id_user_id", table: "user_procurement_memory", columns: new[] { "tenant_id", "user_id" }, unique: true);
        migrationBuilder.Sql("""
            ALTER TABLE "user_procurement_memory" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "user_procurement_memory" FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON "user_procurement_memory"
                USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "user_procurement_memory");
}
