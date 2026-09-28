using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SMMS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffSalary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StaffMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Mobile = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    JoiningDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LeavingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    MonthlySalary = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    PaidLeavePerMonth = table.Column<int>(type: "int", nullable: false),
                    PayDay = table.Column<int>(type: "int", nullable: false),
                    ExpenseCategory = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffMembers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalarySettlements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StaffId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StaffName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    MonthlySalary = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    DaysInMonth = table.Column<int>(type: "int", nullable: false),
                    EmployedDays = table.Column<int>(type: "int", nullable: false),
                    PresentDays = table.Column<int>(type: "int", nullable: false),
                    PaidLeaveAllowed = table.Column<int>(type: "int", nullable: false),
                    LeaveDays = table.Column<int>(type: "int", nullable: false),
                    PaidLeaveDays = table.Column<int>(type: "int", nullable: false),
                    UnpaidLeaveDays = table.Column<int>(type: "int", nullable: false),
                    DailyRate = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    GrossSalary = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    LeaveDeduction = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    CarryIn = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    AdjustmentsTotal = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    AdjustmentsApplied = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    CarryForward = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    NetPaid = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    PaidOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaymentMode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ExpenseId = table.Column<int>(type: "int", nullable: true),
                    ReversedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReversalReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalarySettlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalarySettlements_Expenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "Expenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalarySettlements_StaffMembers_StaffId",
                        column: x => x.StaffId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffAttendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StaffId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffAttendance", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffAttendance_StaffMembers_StaffId",
                        column: x => x.StaffId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalarySettlementLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SettlementId = table.Column<int>(type: "int", nullable: false),
                    StaffPaymentId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalarySettlementLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalarySettlementLines_SalarySettlements_SettlementId",
                        column: x => x.SettlementId,
                        principalTable: "SalarySettlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StaffPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StaffId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PaymentMode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    AdjustAgainstSalary = table.Column<bool>(type: "bit", nullable: false),
                    ExpenseId = table.Column<int>(type: "int", nullable: true),
                    SettlementId = table.Column<int>(type: "int", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffPayments_Expenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "Expenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffPayments_SalarySettlements_SettlementId",
                        column: x => x.SettlementId,
                        principalTable: "SalarySettlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffPayments_StaffMembers_StaffId",
                        column: x => x.StaffId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalarySettlementLines_SettlementId",
                table: "SalarySettlementLines",
                column: "SettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_SalarySettlements_ExpenseId",
                table: "SalarySettlements",
                column: "ExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_SalarySettlements_StaffId_Year_Month",
                table: "SalarySettlements",
                columns: new[] { "StaffId", "Year", "Month" },
                unique: true,
                filter: "[Status] = 'Paid'");

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendance_StaffId_Date",
                table: "StaffAttendance",
                columns: new[] { "StaffId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffPayments_ExpenseId",
                table: "StaffPayments",
                column: "ExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffPayments_SettlementId",
                table: "StaffPayments",
                column: "SettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffPayments_StaffId_Date",
                table: "StaffPayments",
                columns: new[] { "StaffId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalarySettlementLines");

            migrationBuilder.DropTable(
                name: "StaffAttendance");

            migrationBuilder.DropTable(
                name: "StaffPayments");

            migrationBuilder.DropTable(
                name: "SalarySettlements");

            migrationBuilder.DropTable(
                name: "StaffMembers");
        }
    }
}
