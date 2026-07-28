using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SMMS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Collections_MemberId",
                table: "Collections");

            migrationBuilder.AddColumn<bool>(
                name: "AutoGenerateInvoices",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "BillingDay",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "LateFeeApplied",
                table: "Collections",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Collections_MemberId_Year_Month",
                table: "Collections",
                columns: new[] { "MemberId", "Year", "Month" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Collections_MemberId_Year_Month",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "AutoGenerateInvoices",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "BillingDay",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "LateFeeApplied",
                table: "Collections");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_MemberId",
                table: "Collections",
                column: "MemberId");
        }
    }
}
