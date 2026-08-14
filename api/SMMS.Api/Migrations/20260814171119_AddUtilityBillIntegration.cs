using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SMMS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUtilityBillIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UtilityProviders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ProviderName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SupportsAutoFetch = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UtilityProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UtilityConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProviderId = table.Column<int>(type: "int", nullable: false),
                    ConsumerNumber = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ServiceNumber = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    AutoFetchEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastFetchedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastFetchError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UtilityConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UtilityConnections_UtilityProviders_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "UtilityProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UtilityBills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UtilityConnectionId = table.Column<int>(type: "int", nullable: false),
                    BillingMonth = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BillNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BillDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BillAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    UnitsConsumed = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    Arrears = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    ConsumerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RawHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FetchedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UtilityBills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UtilityBills_UtilityConnections_UtilityConnectionId",
                        column: x => x.UtilityConnectionId,
                        principalTable: "UtilityConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UtilityNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UtilityBillId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UtilityNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UtilityNotifications_UtilityBills_UtilityBillId",
                        column: x => x.UtilityBillId,
                        principalTable: "UtilityBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UtilityBills_UtilityConnectionId_BillingMonth",
                table: "UtilityBills",
                columns: new[] { "UtilityConnectionId", "BillingMonth" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UtilityConnections_ProviderId_ConsumerNumber",
                table: "UtilityConnections",
                columns: new[] { "ProviderId", "ConsumerNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UtilityNotifications_UtilityBillId",
                table: "UtilityNotifications",
                column: "UtilityBillId");

            migrationBuilder.CreateIndex(
                name: "IX_UtilityProviders_Code",
                table: "UtilityProviders",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UtilityNotifications");

            migrationBuilder.DropTable(
                name: "UtilityBills");

            migrationBuilder.DropTable(
                name: "UtilityConnections");

            migrationBuilder.DropTable(
                name: "UtilityProviders");
        }
    }
}
