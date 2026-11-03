using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZX.Platform.Identity.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddMenuLauncherMetadata : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "LAUNCHER_DESCRIPTION",
            schema: "ZX_IDENTITY",
            table: "ZX_IDENTITY_MENU",
            type: "NVARCHAR2(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LAUNCHER_DESCRIPTION_EN",
            schema: "ZX_IDENTITY",
            table: "ZX_IDENTITY_MENU",
            type: "NVARCHAR2(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LAUNCHER_TONE",
            schema: "ZX_IDENTITY",
            table: "ZX_IDENTITY_MENU",
            type: "NVARCHAR2(16)",
            maxLength: 16,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "LAUNCHER_DESCRIPTION",
            schema: "ZX_IDENTITY",
            table: "ZX_IDENTITY_MENU");

        migrationBuilder.DropColumn(
            name: "LAUNCHER_DESCRIPTION_EN",
            schema: "ZX_IDENTITY",
            table: "ZX_IDENTITY_MENU");

        migrationBuilder.DropColumn(
            name: "LAUNCHER_TONE",
            schema: "ZX_IDENTITY",
            table: "ZX_IDENTITY_MENU");
    }
}
