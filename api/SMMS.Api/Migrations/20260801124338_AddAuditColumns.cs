using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SMMS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedOn",
                table: "Users",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedOn",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Settings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedOn",
                table: "Settings",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "Settings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedOn",
                table: "Settings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "PaymentProofs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedOn",
                table: "PaymentProofs",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "PaymentProofs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedOn",
                table: "PaymentProofs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Members",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedOn",
                table: "Members",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "Members",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedOn",
                table: "Members",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "MaintenanceComponents",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedOn",
                table: "MaintenanceComponents",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "MaintenanceComponents",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedOn",
                table: "MaintenanceComponents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "MaintenanceComponentRates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedOn",
                table: "MaintenanceComponentRates",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "MaintenanceComponentRates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedOn",
                table: "MaintenanceComponentRates",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "MaintenanceComponentFlatOverrides",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedOn",
                table: "MaintenanceComponentFlatOverrides",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "MaintenanceComponentFlatOverrides",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedOn",
                table: "MaintenanceComponentFlatOverrides",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Expenses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedOn",
                table: "Expenses",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Expenses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "Expenses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedOn",
                table: "Expenses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Complaints",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedOn",
                table: "Complaints",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Complaints",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "Complaints",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedOn",
                table: "Complaints",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Collections",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedOn",
                table: "Collections",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "Collections",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedOn",
                table: "Collections",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "CollectionLines",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedOn",
                table: "CollectionLines",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "CollectionLines",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedOn",
                table: "CollectionLines",
                type: "datetime2",
                nullable: true);

            // Backfill CreatedOn for pre-existing rows (default was 0001-01-01). Use real domain
            // timestamps where a creation time already exists, otherwise the migration run time.
            migrationBuilder.Sql("UPDATE Users SET CreatedOn = CreatedAt WHERE CreatedOn = '0001-01-01';");
            migrationBuilder.Sql("UPDATE Complaints SET CreatedOn = CreatedAt WHERE CreatedOn = '0001-01-01';");
            migrationBuilder.Sql("UPDATE PaymentProofs SET CreatedOn = SubmittedAt WHERE CreatedOn = '0001-01-01';");
            migrationBuilder.Sql("UPDATE Members SET CreatedOn = GETUTCDATE() WHERE CreatedOn = '0001-01-01';");
            migrationBuilder.Sql("UPDATE Settings SET CreatedOn = GETUTCDATE() WHERE CreatedOn = '0001-01-01';");
            migrationBuilder.Sql("UPDATE MaintenanceComponents SET CreatedOn = GETUTCDATE() WHERE CreatedOn = '0001-01-01';");
            migrationBuilder.Sql("UPDATE MaintenanceComponentRates SET CreatedOn = GETUTCDATE() WHERE CreatedOn = '0001-01-01';");
            migrationBuilder.Sql("UPDATE MaintenanceComponentFlatOverrides SET CreatedOn = GETUTCDATE() WHERE CreatedOn = '0001-01-01';");
            migrationBuilder.Sql("UPDATE Expenses SET CreatedOn = GETUTCDATE() WHERE CreatedOn = '0001-01-01';");
            migrationBuilder.Sql("UPDATE Collections SET CreatedOn = GETUTCDATE() WHERE CreatedOn = '0001-01-01';");
            migrationBuilder.Sql("UPDATE CollectionLines SET CreatedOn = GETUTCDATE() WHERE CreatedOn = '0001-01-01';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CreatedOn",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ModifiedOn",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "CreatedOn",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ModifiedOn",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "PaymentProofs");

            migrationBuilder.DropColumn(
                name: "CreatedOn",
                table: "PaymentProofs");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "PaymentProofs");

            migrationBuilder.DropColumn(
                name: "ModifiedOn",
                table: "PaymentProofs");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "CreatedOn",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "ModifiedOn",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "MaintenanceComponents");

            migrationBuilder.DropColumn(
                name: "CreatedOn",
                table: "MaintenanceComponents");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "MaintenanceComponents");

            migrationBuilder.DropColumn(
                name: "ModifiedOn",
                table: "MaintenanceComponents");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "MaintenanceComponentRates");

            migrationBuilder.DropColumn(
                name: "CreatedOn",
                table: "MaintenanceComponentRates");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "MaintenanceComponentRates");

            migrationBuilder.DropColumn(
                name: "ModifiedOn",
                table: "MaintenanceComponentRates");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "MaintenanceComponentFlatOverrides");

            migrationBuilder.DropColumn(
                name: "CreatedOn",
                table: "MaintenanceComponentFlatOverrides");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "MaintenanceComponentFlatOverrides");

            migrationBuilder.DropColumn(
                name: "ModifiedOn",
                table: "MaintenanceComponentFlatOverrides");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "CreatedOn",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "ModifiedOn",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "CreatedOn",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "ModifiedOn",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "CreatedOn",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "ModifiedOn",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "CollectionLines");

            migrationBuilder.DropColumn(
                name: "CreatedOn",
                table: "CollectionLines");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "CollectionLines");

            migrationBuilder.DropColumn(
                name: "ModifiedOn",
                table: "CollectionLines");
        }
    }
}
