using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZX.Platform.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOaPersonnelToUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OA_ACCOUNT",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                type: "NVARCHAR2(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OA_BUSINESS_UNIT",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                type: "NVARCHAR2(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OA_COMPANY",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                type: "NVARCHAR2(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OA_DEPARTMENT_CODE",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                type: "NVARCHAR2(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OA_DEPARTMENT_NAME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                type: "NVARCHAR2(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OA_DEPARTMENT_RAW",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                type: "NVARCHAR2(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OA_DIRECTORY_STATUS",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                type: "NVARCHAR2(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OA_DISPLAY_NAME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                type: "NVARCHAR2(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OA_DISTINGUISHED_NAME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                type: "NCLOB",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OA_EMAIL",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                type: "NVARCHAR2(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OA_MANAGER_DN",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                type: "NCLOB",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OA_NAME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                type: "NVARCHAR2(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OA_SYNCED_AT_UTC",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                type: "TIMESTAMP(7) WITH TIME ZONE",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZID_USER_OA_BU_DEPT",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                columns: new[] { "OA_BUSINESS_UNIT", "OA_DEPARTMENT_CODE" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ZID_USER_OA_STATUS",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                sql: "OA_DIRECTORY_STATUS IS NULL OR OA_DIRECTORY_STATUS IN ('Enabled', 'Disabled', 'Unknown')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ZID_USER_OA_BU_DEPT",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ZID_USER_OA_STATUS",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "OA_ACCOUNT",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "OA_BUSINESS_UNIT",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "OA_COMPANY",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "OA_DEPARTMENT_CODE",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "OA_DEPARTMENT_NAME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "OA_DEPARTMENT_RAW",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "OA_DIRECTORY_STATUS",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "OA_DISPLAY_NAME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "OA_DISTINGUISHED_NAME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "OA_EMAIL",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "OA_MANAGER_DN",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "OA_NAME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "OA_SYNCED_AT_UTC",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");
        }
    }
}
