using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SMMS.Api.Migrations
{
    /// <inheritdoc />
    public partial class BookLiabilityCostWhenIncurred : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "SocietyLiabilities",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "FundedByLiabilityId",
                table: "Expenses",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_FundedByLiabilityId",
                table: "Expenses",
                column: "FundedByLiabilityId");

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_SocietyLiabilities_FundedByLiabilityId",
                table: "Expenses",
                column: "FundedByLiabilityId",
                principalTable: "SocietyLiabilities",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_SocietyLiabilities_FundedByLiabilityId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_FundedByLiabilityId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "SocietyLiabilities");

            migrationBuilder.DropColumn(
                name: "FundedByLiabilityId",
                table: "Expenses");
        }
    }
}
