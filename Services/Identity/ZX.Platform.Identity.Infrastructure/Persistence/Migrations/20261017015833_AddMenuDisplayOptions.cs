using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZX.Platform.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuDisplayOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AFFIX",
                table: "ZX_IDENTITY_MENU",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SHOW_IN_MENU",
                table: "ZX_IDENTITY_MENU",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "SHOW_IN_TAGS",
                table: "ZX_IDENTITY_MENU",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AFFIX",
                table: "ZX_IDENTITY_MENU");

            migrationBuilder.DropColumn(
                name: "SHOW_IN_MENU",
                table: "ZX_IDENTITY_MENU");

            migrationBuilder.DropColumn(
                name: "SHOW_IN_TAGS",
                table: "ZX_IDENTITY_MENU");
        }
    }
}
