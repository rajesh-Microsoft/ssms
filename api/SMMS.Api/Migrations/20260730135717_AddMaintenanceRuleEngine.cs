using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SMMS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceRuleEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MaintenanceCalcMethod",
                table: "Settings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "AreaSqFt",
                table: "Members",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "FlatType",
                table: "Members",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tower",
                table: "Members",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CollectionLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CollectionId = table.Column<int>(type: "int", nullable: false),
                    ComponentId = table.Column<int>(type: "int", nullable: true),
                    ComponentName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CollectionLines_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceComponents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Method = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,4)", nullable: false),
                    PercentageValue = table.Column<decimal>(type: "decimal(6,3)", nullable: true),
                    PercentageBaseComponentId = table.Column<int>(type: "int", nullable: true),
                    ApplyToAllFlats = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceComponents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceComponentFlatOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ComponentId = table.Column<int>(type: "int", nullable: false),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    IsApplicable = table.Column<bool>(type: "bit", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,4)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceComponentFlatOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceComponentFlatOverrides_MaintenanceComponents_ComponentId",
                        column: x => x.ComponentId,
                        principalTable: "MaintenanceComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaintenanceComponentFlatOverrides_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceComponentRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ComponentId = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceComponentRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceComponentRates_MaintenanceComponents_ComponentId",
                        column: x => x.ComponentId,
                        principalTable: "MaintenanceComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionLines_CollectionId",
                table: "CollectionLines",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceComponentFlatOverrides_ComponentId_MemberId",
                table: "MaintenanceComponentFlatOverrides",
                columns: new[] { "ComponentId", "MemberId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceComponentFlatOverrides_MemberId",
                table: "MaintenanceComponentFlatOverrides",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceComponentRates_ComponentId_Key",
                table: "MaintenanceComponentRates",
                columns: new[] { "ComponentId", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CollectionLines");

            migrationBuilder.DropTable(
                name: "MaintenanceComponentFlatOverrides");

            migrationBuilder.DropTable(
                name: "MaintenanceComponentRates");

            migrationBuilder.DropTable(
                name: "MaintenanceComponents");

            migrationBuilder.DropColumn(
                name: "MaintenanceCalcMethod",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "AreaSqFt",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "FlatType",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "Tower",
                table: "Members");
        }
    }
}
