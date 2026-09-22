using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Chat.Migrations
{
    /// <summary>
    /// ADR-030 (Ask Raffa interview): one nullable <c>jsonb</c> column on
    /// <c>conversation_message</c>. A Raffa <c>Interview</c> row stores its questions, options and
    /// each option's server-side resolution (plus the consumed-at stamp of a single-use consent);
    /// the You row that answers an interview stores which message/question/option it answered.
    /// Every other row leaves it null. A dedicated column rather than a discriminator inside
    /// <c>actions_json</c>: that array is contractually catalog actions only (label/href/kind), and
    /// every consumer of it assumes so.
    /// </summary>
    public partial class AddInterviewJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "interview_json",
                table: "conversation_message",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "interview_json",
                table: "conversation_message");
        }
    }
}
