using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SMMS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUtilityBillPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExpenseId",
                table: "UtilityBills",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidOn",
                table: "UtilityBills",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentReference",
                table: "UtilityBills",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UtilityBills_ExpenseId",
                table: "UtilityBills",
                column: "ExpenseId");

            migrationBuilder.AddForeignKey(
                name: "FK_UtilityBills_Expenses_ExpenseId",
                table: "UtilityBills",
                column: "ExpenseId",
                principalTable: "Expenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UtilityBills_Expenses_ExpenseId",
                table: "UtilityBills");

            migrationBuilder.DropIndex(
                name: "IX_UtilityBills_ExpenseId",
                table: "UtilityBills");

            migrationBuilder.DropColumn(
                name: "ExpenseId",
                table: "UtilityBills");

            migrationBuilder.DropColumn(
                name: "PaidOn",
                table: "UtilityBills");

            migrationBuilder.DropColumn(
                name: "PaymentReference",
                table: "UtilityBills");
        }
    }
}
