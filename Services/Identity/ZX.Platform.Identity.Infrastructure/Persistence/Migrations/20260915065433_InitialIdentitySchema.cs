using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZX.Platform.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentitySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ZX_IDENTITY_FACTORY",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CODE = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NAME = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IS_ENABLED = table.Column<bool>(type: "bit", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZX_IDENTITY_FACTORY", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "ZX_IDENTITY_USER",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ACCOUNT = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NORMALIZED_ACCOUNT = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DISPLAY_NAME = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PASSWORD_HASH = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IS_ENABLED = table.Column<bool>(type: "bit", nullable: false),
                    ACCESS_FAILED_COUNT = table.Column<int>(type: "int", nullable: false),
                    LOCKOUT_END_UTC = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZX_IDENTITY_USER", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "ZX_IDENTITY_USER_FACTORY",
                columns: table => new
                {
                    USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FACTORY_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IS_DEFAULT = table.Column<bool>(type: "bit", nullable: false),
                    IS_ENABLED = table.Column<bool>(type: "bit", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZX_IDENTITY_USER_FACTORY", x => new { x.USER_ID, x.FACTORY_ID });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ZX_IDENTITY_FACTORY_CODE",
                table: "ZX_IDENTITY_FACTORY",
                column: "CODE",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZX_IDENTITY_USER_NORMALIZED_ACCOUNT",
                table: "ZX_IDENTITY_USER",
                column: "NORMALIZED_ACCOUNT",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZX_IDENTITY_USER_FACTORY_FACTORY_ID",
                table: "ZX_IDENTITY_USER_FACTORY",
                column: "FACTORY_ID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ZX_IDENTITY_USER_FACTORY");

            migrationBuilder.DropTable(
                name: "ZX_IDENTITY_FACTORY");

            migrationBuilder.DropTable(
                name: "ZX_IDENTITY_USER");
        }
    }
}
