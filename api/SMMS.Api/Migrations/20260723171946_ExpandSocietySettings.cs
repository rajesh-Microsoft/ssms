using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SMMS.Api.Migrations
{
    /// <inheritdoc />
    public partial class ExpandSocietySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApplicationTitle",
                table: "Settings",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DueDay",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FinancialYear",
                table: "Settings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GraceDays",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Gst",
                table: "Settings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LateFee",
                table: "Settings",
                type: "decimal(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "LogoBase64",
                table: "Settings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Pan",
                table: "Settings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryColor",
                table: "Settings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RegistrationNumber",
                table: "Settings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondaryColor",
                table: "Settings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApplicationTitle",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "DueDay",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "FinancialYear",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "GraceDays",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Gst",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "LateFee",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "LogoBase64",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Pan",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "PrimaryColor",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "RegistrationNumber",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SecondaryColor",
                table: "Settings");
        }
    }
}
