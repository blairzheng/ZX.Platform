using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZX.Platform.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVietnameseMenuTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LAUNCHER_DESCRIPTION_VI",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_MENU",
                type: "NVARCHAR2(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TITLE_VI",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_MENU",
                type: "NVARCHAR2(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LAUNCHER_DESCRIPTION_VI",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_MENU");

            migrationBuilder.DropColumn(
                name: "TITLE_VI",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_MENU");
        }
    }
}
