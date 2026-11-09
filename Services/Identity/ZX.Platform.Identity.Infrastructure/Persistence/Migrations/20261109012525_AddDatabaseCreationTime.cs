using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZX.Platform.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDatabaseCreationTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CREATE_TIME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER_SBU",
                type: "DATE",
                nullable: false,
                defaultValueSql: "SYSDATE");

            migrationBuilder.AddColumn<DateTime>(
                name: "CREATE_TIME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER_ROLE",
                type: "DATE",
                nullable: false,
                defaultValueSql: "SYSDATE");

            migrationBuilder.AddColumn<DateTime>(
                name: "CREATE_TIME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER_APPROVAL_AUDIT",
                type: "DATE",
                nullable: false,
                defaultValueSql: "SYSDATE");

            migrationBuilder.AddColumn<DateTime>(
                name: "CREATE_TIME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                type: "DATE",
                nullable: false,
                defaultValueSql: "SYSDATE");

            migrationBuilder.AddColumn<DateTime>(
                name: "CREATE_TIME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_SBU",
                type: "DATE",
                nullable: false,
                defaultValueSql: "SYSDATE");

            migrationBuilder.AddColumn<DateTime>(
                name: "CREATE_TIME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_ROLE_PERMISSION",
                type: "DATE",
                nullable: false,
                defaultValueSql: "SYSDATE");

            migrationBuilder.AddColumn<DateTime>(
                name: "CREATE_TIME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_ROLE",
                type: "DATE",
                nullable: false,
                defaultValueSql: "SYSDATE");

            migrationBuilder.AddColumn<DateTime>(
                name: "CREATE_TIME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_PERMISSION",
                type: "DATE",
                nullable: false,
                defaultValueSql: "SYSDATE");

            migrationBuilder.AddColumn<DateTime>(
                name: "CREATE_TIME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_MENU",
                type: "DATE",
                nullable: false,
                defaultValueSql: "SYSDATE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CREATE_TIME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER_SBU");

            migrationBuilder.DropColumn(
                name: "CREATE_TIME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER_ROLE");

            migrationBuilder.DropColumn(
                name: "CREATE_TIME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER_APPROVAL_AUDIT");

            migrationBuilder.DropColumn(
                name: "CREATE_TIME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "CREATE_TIME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_SBU");

            migrationBuilder.DropColumn(
                name: "CREATE_TIME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_ROLE_PERMISSION");

            migrationBuilder.DropColumn(
                name: "CREATE_TIME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_ROLE");

            migrationBuilder.DropColumn(
                name: "CREATE_TIME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_PERMISSION");

            migrationBuilder.DropColumn(
                name: "CREATE_TIME",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_MENU");
        }
    }
}
