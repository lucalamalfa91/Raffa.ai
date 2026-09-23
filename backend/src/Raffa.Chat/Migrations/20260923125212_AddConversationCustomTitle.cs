using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Chat.Migrations
{
    /// <summary>
    /// Chat rename: one nullable <c>custom_title</c> column on <c>conversation</c>, the name the
    /// user gave the chat (<c>PATCH /api/conversations/{id}</c>). Separate from <c>title</c> (the
    /// first question) so clearing a name restores the automatic title. Every existing row stays
    /// null -- nothing was renamed before this column existed.
    /// </summary>
    public partial class AddConversationCustomTitle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "custom_title",
                table: "conversation",
                type: "character varying(48)",
                maxLength: 48,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "custom_title",
                table: "conversation");
        }
    }
}
