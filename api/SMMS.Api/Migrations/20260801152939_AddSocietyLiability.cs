using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SMMS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSocietyLiability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SocietyLiabilities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Source = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MemberId = table.Column<int>(type: "int", nullable: true),
                    ContributorName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    SettledAmount = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocietyLiabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocietyLiabilities_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SocietyLiabilitySettlements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LiabilityId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ExpenseId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocietyLiabilitySettlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocietyLiabilitySettlements_Expenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "Expenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SocietyLiabilitySettlements_SocietyLiabilities_LiabilityId",
                        column: x => x.LiabilityId,
                        principalTable: "SocietyLiabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SocietyLiabilities_MemberId",
                table: "SocietyLiabilities",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_SocietyLiabilities_Status_Date",
                table: "SocietyLiabilities",
                columns: new[] { "Status", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_SocietyLiabilitySettlements_ExpenseId",
                table: "SocietyLiabilitySettlements",
                column: "ExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_SocietyLiabilitySettlements_LiabilityId",
                table: "SocietyLiabilitySettlements",
                column: "LiabilityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SocietyLiabilitySettlements");

            migrationBuilder.DropTable(
                name: "SocietyLiabilities");
        }
    }
}
