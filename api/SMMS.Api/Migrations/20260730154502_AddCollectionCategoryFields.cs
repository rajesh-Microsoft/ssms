using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SMMS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionCategoryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CategoryType",
                table: "MaintenanceComponents",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Recurring");

            migrationBuilder.AddColumn<string>(
                name: "Frequency",
                table: "MaintenanceComponents",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Monthly");

            migrationBuilder.AddColumn<bool>(
                name: "LateFeeApplicable",
                table: "MaintenanceComponents",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "TaxApplicable",
                table: "MaintenanceComponents",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CategoryType",
                table: "MaintenanceComponents");

            migrationBuilder.DropColumn(
                name: "Frequency",
                table: "MaintenanceComponents");

            migrationBuilder.DropColumn(
                name: "LateFeeApplicable",
                table: "MaintenanceComponents");

            migrationBuilder.DropColumn(
                name: "TaxApplicable",
                table: "MaintenanceComponents");
        }
    }
}
