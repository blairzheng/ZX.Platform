using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZX.Platform.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceFactoriesWithSbus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Retire only the built-in factory menu nodes; user/role/menu records remain intact.
            migrationBuilder.Sql("DELETE FROM \"ZX_IDENTITY\".\"ZX_IDENTITY_MENU\" WHERE \"CODE\" IN ('factories', 'permission_identity_user_factories', 'permission_identity_factory_read', 'permission_identity_factory_create', 'permission_identity_factory_rename', 'permission_identity_factory_status', 'permission_identity_factory_delete', 'permission_identity_factory_restore')");

            migrationBuilder.DropTable(
                name: "ZX_IDENTITY_FACTORY",
                schema: "ZX_IDENTITY");

            migrationBuilder.DropTable(
                name: "ZX_IDENTITY_USER_FACTORY",
                schema: "ZX_IDENTITY");

            migrationBuilder.CreateTable(
                name: "ZX_IDENTITY_SBU",
                schema: "ZX_IDENTITY",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    CODE = table.Column<string>(type: "NVARCHAR2(50)", maxLength: 50, nullable: false),
                    NAME = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    IS_ENABLED = table.Column<bool>(type: "NUMBER(1)", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    CREATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    UPDATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    IS_DELETED = table.Column<bool>(type: "NUMBER(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZX_IDENTITY_SBU", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "ZX_IDENTITY_USER_SBU",
                schema: "ZX_IDENTITY",
                columns: table => new
                {
                    USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    SBU_ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    IS_DEFAULT = table.Column<bool>(type: "NUMBER(1)", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    CREATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    UPDATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZX_IDENTITY_USER_SBU", x => new { x.USER_ID, x.SBU_ID });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ZX_IDENTITY_SBU_CODE",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_SBU",
                column: "CODE",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZX_IDENTITY_USER_SBU_SBU_ID",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER_SBU",
                column: "SBU_ID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ZX_IDENTITY_SBU",
                schema: "ZX_IDENTITY");

            migrationBuilder.DropTable(
                name: "ZX_IDENTITY_USER_SBU",
                schema: "ZX_IDENTITY");

            migrationBuilder.CreateTable(
                name: "ZX_IDENTITY_FACTORY",
                schema: "ZX_IDENTITY",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    CODE = table.Column<string>(type: "NVARCHAR2(50)", maxLength: 50, nullable: false),
                    CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    CREATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    IS_DELETED = table.Column<bool>(type: "NUMBER(1)", nullable: false),
                    IS_ENABLED = table.Column<bool>(type: "NUMBER(1)", nullable: false),
                    NAME = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    UPDATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    UPDATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZX_IDENTITY_FACTORY", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "ZX_IDENTITY_USER_FACTORY",
                schema: "ZX_IDENTITY",
                columns: table => new
                {
                    USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    FACTORY_ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    CREATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    IS_DEFAULT = table.Column<bool>(type: "NUMBER(1)", nullable: false),
                    UPDATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    UPDATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZX_IDENTITY_USER_FACTORY", x => new { x.USER_ID, x.FACTORY_ID });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ZX_IDENTITY_FACTORY_CODE",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_FACTORY",
                column: "CODE",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZX_IDENTITY_USER_FACTORY_FACTORY_ID",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER_FACTORY",
                column: "FACTORY_ID");
        }
    }
}
