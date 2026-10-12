using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZX.Platform.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlignUserFactoryQueryFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CREATED_BY_USER_ID",
                table: "ZX_IDENTITY_USER_FACTORY",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UPDATED_AT_UTC",
                table: "ZX_IDENTITY_USER_FACTORY",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UPDATED_BY_USER_ID",
                table: "ZX_IDENTITY_USER_FACTORY",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CREATED_BY_USER_ID",
                table: "ZX_IDENTITY_USER",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IS_DELETED",
                table: "ZX_IDENTITY_USER",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UPDATED_AT_UTC",
                table: "ZX_IDENTITY_USER",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UPDATED_BY_USER_ID",
                table: "ZX_IDENTITY_USER",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CREATED_BY_USER_ID",
                table: "ZX_IDENTITY_FACTORY",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IS_DELETED",
                table: "ZX_IDENTITY_FACTORY",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UPDATED_AT_UTC",
                table: "ZX_IDENTITY_FACTORY",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UPDATED_BY_USER_ID",
                table: "ZX_IDENTITY_FACTORY",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CREATED_BY_USER_ID",
                table: "ZX_IDENTITY_USER_FACTORY");

            migrationBuilder.DropColumn(
                name: "UPDATED_AT_UTC",
                table: "ZX_IDENTITY_USER_FACTORY");

            migrationBuilder.DropColumn(
                name: "UPDATED_BY_USER_ID",
                table: "ZX_IDENTITY_USER_FACTORY");

            migrationBuilder.DropColumn(
                name: "CREATED_BY_USER_ID",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "IS_DELETED",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "UPDATED_AT_UTC",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "UPDATED_BY_USER_ID",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "CREATED_BY_USER_ID",
                table: "ZX_IDENTITY_FACTORY");

            migrationBuilder.DropColumn(
                name: "IS_DELETED",
                table: "ZX_IDENTITY_FACTORY");

            migrationBuilder.DropColumn(
                name: "UPDATED_AT_UTC",
                table: "ZX_IDENTITY_FACTORY");

            migrationBuilder.DropColumn(
                name: "UPDATED_BY_USER_ID",
                table: "ZX_IDENTITY_FACTORY");
        }
    }
}
