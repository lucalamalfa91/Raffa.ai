using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Raffa.Chat.Infrastructure;

#nullable disable

namespace Raffa.Chat.Migrations;

[DbContext(typeof(ChatDbContext))]
[Migration("20261105120000_AddConversationState")]
public partial class AddConversationState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(
            name: "state_json", table: "conversation", type: "jsonb", nullable: false,
            defaultValue: "{}");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "state_json", table: "conversation");
}
