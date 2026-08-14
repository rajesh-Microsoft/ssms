using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SMMS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPhonePeToggle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PhonePeEnabled",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhonePeEnabled",
                table: "Settings");
        }
    }
}
