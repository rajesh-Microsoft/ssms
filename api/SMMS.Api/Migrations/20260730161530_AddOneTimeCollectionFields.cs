using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SMMS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOneTimeCollectionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "Collections",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CollectionType",
                table: "Collections",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Monthly");

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Collections",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "CollectionType",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "Collections");
        }
    }
}
