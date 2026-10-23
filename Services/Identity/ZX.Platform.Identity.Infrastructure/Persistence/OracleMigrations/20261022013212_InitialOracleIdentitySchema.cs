using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZX.Platform.Identity.Infrastructure.Persistence.OracleMigrations
{
    /// <inheritdoc />
    public partial class InitialOracleIdentitySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ZX_IDENTITY_FACTORY",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    CODE = table.Column<string>(type: "NVARCHAR2(50)", maxLength: 50, nullable: false),
                    NAME = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    IS_ENABLED = table.Column<bool>(type: "BOOLEAN", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    CREATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    UPDATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    IS_DELETED = table.Column<bool>(type: "BOOLEAN", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZX_IDENTITY_FACTORY", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "ZX_IDENTITY_MENU",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    PARENT_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    CODE = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    KIND = table.Column<string>(type: "NVARCHAR2(16)", maxLength: 16, nullable: false),
                    TITLE = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    TITLE_EN = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: true),
                    ICON = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: true),
                    PATH = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: true),
                    COMPONENT = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: true),
                    PERMISSION_CODE = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: true),
                    SORT = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    KEEP_ALIVE = table.Column<bool>(type: "BOOLEAN", nullable: false),
                    SHOW_IN_MENU = table.Column<bool>(type: "BOOLEAN", nullable: false),
                    SHOW_IN_TAGS = table.Column<bool>(type: "BOOLEAN", nullable: false),
                    AFFIX = table.Column<bool>(type: "BOOLEAN", nullable: false),
                    IS_ENABLED = table.Column<bool>(type: "BOOLEAN", nullable: false),
                    IS_BUILT_IN = table.Column<bool>(type: "BOOLEAN", nullable: false),
                    ACTIVE_ACTION_PERMISSION = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: true, computedColumnSql: "CASE WHEN \"KIND\" = N'action' AND \"IS_DELETED\" IS FALSE THEN \"PERMISSION_CODE\" END", stored: false),
                    ACTIVE_PAGE_PATH = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: true, computedColumnSql: "CASE WHEN \"KIND\" = N'page' AND \"IS_DELETED\" IS FALSE THEN \"PATH\" END", stored: false),
                    CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    CREATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    UPDATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    IS_DELETED = table.Column<bool>(type: "BOOLEAN", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZX_IDENTITY_MENU", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "ZX_IDENTITY_PERMISSION",
                columns: table => new
                {
                    CODE = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    IS_ACTIVE = table.Column<bool>(type: "BOOLEAN", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZX_IDENTITY_PERMISSION", x => x.CODE);
                });

            migrationBuilder.CreateTable(
                name: "ZX_IDENTITY_ROLE",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    CODE = table.Column<string>(type: "NVARCHAR2(50)", maxLength: 50, nullable: false),
                    NAME = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    DESCRIPTION = table.Column<string>(type: "NVARCHAR2(500)", maxLength: 500, nullable: true),
                    IS_ENABLED = table.Column<bool>(type: "BOOLEAN", nullable: false),
                    IS_BUILT_IN = table.Column<bool>(type: "BOOLEAN", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    CREATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    UPDATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    IS_DELETED = table.Column<bool>(type: "BOOLEAN", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZX_IDENTITY_ROLE", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "ZX_IDENTITY_ROLE_PERMISSION",
                columns: table => new
                {
                    ROLE_ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    PERMISSION_CODE = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    CREATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    UPDATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZX_IDENTITY_ROLE_PERMISSION", x => new { x.ROLE_ID, x.PERMISSION_CODE });
                });

            migrationBuilder.CreateTable(
                name: "ZX_IDENTITY_USER",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    ACCOUNT = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    NORMALIZED_ACCOUNT = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    DISPLAY_NAME = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    PASSWORD_HASH = table.Column<string>(type: "NVARCHAR2(500)", maxLength: 500, nullable: false),
                    IS_ENABLED = table.Column<bool>(type: "BOOLEAN", nullable: false),
                    ACCESS_FAILED_COUNT = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    LOCKOUT_END_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    CREATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    UPDATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    IS_DELETED = table.Column<bool>(type: "BOOLEAN", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZX_IDENTITY_USER", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "ZX_IDENTITY_USER_FACTORY",
                columns: table => new
                {
                    USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    FACTORY_ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    IS_DEFAULT = table.Column<bool>(type: "BOOLEAN", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    CREATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    UPDATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZX_IDENTITY_USER_FACTORY", x => new { x.USER_ID, x.FACTORY_ID });
                });

            migrationBuilder.CreateTable(
                name: "ZX_IDENTITY_USER_ROLE",
                columns: table => new
                {
                    USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    ROLE_ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    CREATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    UPDATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZX_IDENTITY_USER_ROLE", x => new { x.USER_ID, x.ROLE_ID });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ZX_IDENTITY_FACTORY_CODE",
                table: "ZX_IDENTITY_FACTORY",
                column: "CODE",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZX_IDENTITY_MENU_ACTIVE_ACTION_PERMISSION",
                table: "ZX_IDENTITY_MENU",
                column: "ACTIVE_ACTION_PERMISSION",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZX_IDENTITY_MENU_ACTIVE_PAGE_PATH",
                table: "ZX_IDENTITY_MENU",
                column: "ACTIVE_PAGE_PATH",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZX_IDENTITY_MENU_CODE",
                table: "ZX_IDENTITY_MENU",
                column: "CODE",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZX_IDENTITY_MENU_PARENT_ID",
                table: "ZX_IDENTITY_MENU",
                column: "PARENT_ID");

            migrationBuilder.CreateIndex(
                name: "IX_ZX_IDENTITY_ROLE_CODE",
                table: "ZX_IDENTITY_ROLE",
                column: "CODE",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZX_IDENTITY_ROLE_PERMISSION_PERMISSION_CODE",
                table: "ZX_IDENTITY_ROLE_PERMISSION",
                column: "PERMISSION_CODE");

            migrationBuilder.CreateIndex(
                name: "IX_ZX_IDENTITY_USER_NORMALIZED_ACCOUNT",
                table: "ZX_IDENTITY_USER",
                column: "NORMALIZED_ACCOUNT",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZX_IDENTITY_USER_FACTORY_FACTORY_ID",
                table: "ZX_IDENTITY_USER_FACTORY",
                column: "FACTORY_ID");

            migrationBuilder.CreateIndex(
                name: "IX_ZX_IDENTITY_USER_ROLE_ROLE_ID",
                table: "ZX_IDENTITY_USER_ROLE",
                column: "ROLE_ID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ZX_IDENTITY_FACTORY");

            migrationBuilder.DropTable(
                name: "ZX_IDENTITY_MENU");

            migrationBuilder.DropTable(
                name: "ZX_IDENTITY_PERMISSION");

            migrationBuilder.DropTable(
                name: "ZX_IDENTITY_ROLE");

            migrationBuilder.DropTable(
                name: "ZX_IDENTITY_ROLE_PERMISSION");

            migrationBuilder.DropTable(
                name: "ZX_IDENTITY_USER");

            migrationBuilder.DropTable(
                name: "ZX_IDENTITY_USER_FACTORY");

            migrationBuilder.DropTable(
                name: "ZX_IDENTITY_USER_ROLE");
        }
    }
}
