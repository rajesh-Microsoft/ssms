using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SMMS.Api.Migrations.Control
{
    /// <inheritdoc />
    public partial class AddSocietyOnboardingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Societies",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdminEmail",
                table: "Societies",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdminName",
                table: "Societies",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Societies",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address",
                table: "Societies");

            migrationBuilder.DropColumn(
                name: "AdminEmail",
                table: "Societies");

            migrationBuilder.DropColumn(
                name: "AdminName",
                table: "Societies");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Societies");
        }
    }
}
