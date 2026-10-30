using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZX.Platform.LowCode.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialLowCodeMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LC_META_APP",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    CODE = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    NAME = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    NAME_EN = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: true),
                    STATUS = table.Column<string>(type: "NVARCHAR2(16)", maxLength: 16, nullable: false),
                    REVISION = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    CREATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    UPDATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    IS_DELETED = table.Column<bool>(type: "NUMBER(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LC_META_APP", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "LC_META_DATASET",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    CODE = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    NAME = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    NAME_EN = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: true),
                    STATUS = table.Column<string>(type: "NVARCHAR2(16)", maxLength: 16, nullable: false),
                    REVISION = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    CREATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    UPDATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    IS_DELETED = table.Column<bool>(type: "NUMBER(1)", nullable: false),
                    APP_CODE = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    DEFINITION_JSON = table.Column<string>(type: "CLOB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LC_META_DATASET", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "LC_META_ENTITY",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    CODE = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    NAME = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    NAME_EN = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: true),
                    STATUS = table.Column<string>(type: "NVARCHAR2(16)", maxLength: 16, nullable: false),
                    REVISION = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    CREATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    UPDATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    IS_DELETED = table.Column<bool>(type: "NUMBER(1)", nullable: false),
                    APP_CODE = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    DRAFT_JSON = table.Column<string>(type: "CLOB", nullable: true),
                    PUBLISHED_JSON = table.Column<string>(type: "CLOB", nullable: true),
                    PUBLISHED_VERSION = table.Column<int>(type: "NUMBER(10)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LC_META_ENTITY", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "LC_META_PAGE",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    CODE = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    NAME = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    NAME_EN = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: true),
                    STATUS = table.Column<string>(type: "NVARCHAR2(16)", maxLength: 16, nullable: false),
                    REVISION = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    CREATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    UPDATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    IS_DELETED = table.Column<bool>(type: "NUMBER(1)", nullable: false),
                    APP_CODE = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    DRAFT_JSON = table.Column<string>(type: "CLOB", nullable: true),
                    PUBLISHED_JSON = table.Column<string>(type: "CLOB", nullable: true),
                    PUBLISHED_VERSION = table.Column<int>(type: "NUMBER(10)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LC_META_PAGE", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "LC_META_VERSION",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    OWNER_TYPE = table.Column<string>(type: "NVARCHAR2(16)", maxLength: 16, nullable: false),
                    OWNER_CODE = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    VERSION = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    PAYLOAD_JSON = table.Column<string>(type: "CLOB", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    CREATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LC_META_VERSION", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "LC_SCHEMA_CHANGE",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    APP_CODE = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    ENTITY_CODE = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    KIND = table.Column<string>(type: "NVARCHAR2(32)", maxLength: 32, nullable: false),
                    STATUS = table.Column<string>(type: "NVARCHAR2(16)", maxLength: 16, nullable: false),
                    PLAN_JSON = table.Column<string>(type: "CLOB", nullable: false),
                    SCRIPT_SQL = table.Column<string>(type: "CLOB", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    CREATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    EXECUTED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    VERIFIED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    ERROR_MESSAGE = table.Column<string>(type: "NVARCHAR2(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LC_SCHEMA_CHANGE", x => x.ID);
                });

            migrationBuilder.CreateIndex(
                name: "UX_LC_META_APP_CODE",
                table: "LC_META_APP",
                column: "CODE",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_LC_META_DATASET_CODE",
                table: "LC_META_DATASET",
                columns: new[] { "APP_CODE", "CODE" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_LC_META_ENTITY_CODE",
                table: "LC_META_ENTITY",
                columns: new[] { "APP_CODE", "CODE" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_LC_META_PAGE_CODE",
                table: "LC_META_PAGE",
                columns: new[] { "APP_CODE", "CODE" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_LC_META_VERSION_NO",
                table: "LC_META_VERSION",
                columns: new[] { "OWNER_TYPE", "OWNER_CODE", "VERSION" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LC_SCHEMA_CHANGE_ENTITY",
                table: "LC_SCHEMA_CHANGE",
                columns: new[] { "ENTITY_CODE", "STATUS" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LC_META_APP");

            migrationBuilder.DropTable(
                name: "LC_META_DATASET");

            migrationBuilder.DropTable(
                name: "LC_META_ENTITY");

            migrationBuilder.DropTable(
                name: "LC_META_PAGE");

            migrationBuilder.DropTable(
                name: "LC_META_VERSION");

            migrationBuilder.DropTable(
                name: "LC_SCHEMA_CHANGE");
        }
    }
}
