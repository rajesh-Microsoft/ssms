using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SMMS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAdvanceWallet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AdvanceBalance",
                table: "Members",
                type: "decimal(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "AdvanceMode",
                table: "Members",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Auto");

            migrationBuilder.AddColumn<decimal>(
                name: "AmountPaid",
                table: "Collections",
                type: "decimal(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "AdvanceLedger",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CollectionId = table.Column<int>(type: "int", nullable: true),
                    PaymentProofId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvanceLedger", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdvanceLedger_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceLedger_MemberId_Date",
                table: "AdvanceLedger",
                columns: new[] { "MemberId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdvanceLedger");

            migrationBuilder.DropColumn(
                name: "AdvanceBalance",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "AdvanceMode",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "AmountPaid",
                table: "Collections");
        }
    }
}
