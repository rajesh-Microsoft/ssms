using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SMMS.Api.Migrations
{
    /// <inheritdoc />
    public partial class LinkStaffPaymentToReimbursement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReimbursementId",
                table: "StaffPayments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffPayments_ReimbursementId",
                table: "StaffPayments",
                column: "ReimbursementId",
                unique: true,
                filter: "[ReimbursementId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_StaffPayments_ReimbursementRequests_ReimbursementId",
                table: "StaffPayments",
                column: "ReimbursementId",
                principalTable: "ReimbursementRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StaffPayments_ReimbursementRequests_ReimbursementId",
                table: "StaffPayments");

            migrationBuilder.DropIndex(
                name: "IX_StaffPayments_ReimbursementId",
                table: "StaffPayments");

            migrationBuilder.DropColumn(
                name: "ReimbursementId",
                table: "StaffPayments");
        }
    }
}
