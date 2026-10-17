using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZX.Platform.Identity.Infrastructure.Persistence.Migrations;

public partial class AddRolesAndPermissions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ZX_IDENTITY_PERMISSION",
            columns: table => new
            {
                CODE = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                IS_ACTIVE = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ZX_IDENTITY_PERMISSION", x => x.CODE));

        migrationBuilder.CreateTable(
            name: "ZX_IDENTITY_ROLE",
            columns: table => new
            {
                ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CODE = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                NAME = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                DESCRIPTION = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                IS_ENABLED = table.Column<bool>(type: "bit", nullable: false),
                IS_BUILT_IN = table.Column<bool>(type: "bit", nullable: false),
                IS_DELETED = table.Column<bool>(type: "bit", nullable: false),
                CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CREATED_BY_USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UPDATED_AT_UTC = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                UPDATED_BY_USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_ZX_IDENTITY_ROLE", x => x.ID));

        migrationBuilder.CreateTable(
            name: "ZX_IDENTITY_ROLE_PERMISSION",
            columns: table => new
            {
                ROLE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PERMISSION_CODE = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CREATED_BY_USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UPDATED_AT_UTC = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                UPDATED_BY_USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_ZX_IDENTITY_ROLE_PERMISSION", x => new { x.ROLE_ID, x.PERMISSION_CODE }));

        migrationBuilder.CreateTable(
            name: "ZX_IDENTITY_USER_ROLE",
            columns: table => new
            {
                USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ROLE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CREATED_BY_USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UPDATED_AT_UTC = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                UPDATED_BY_USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_ZX_IDENTITY_USER_ROLE", x => new { x.USER_ID, x.ROLE_ID }));

        migrationBuilder.CreateIndex("IX_ZX_IDENTITY_ROLE_CODE", "ZX_IDENTITY_ROLE", "CODE", unique: true);
        migrationBuilder.CreateIndex("IX_ZX_IDENTITY_ROLE_PERMISSION_PERMISSION_CODE", "ZX_IDENTITY_ROLE_PERMISSION", "PERMISSION_CODE");
        migrationBuilder.CreateIndex("IX_ZX_IDENTITY_USER_ROLE_ROLE_ID", "ZX_IDENTITY_USER_ROLE", "ROLE_ID");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("ZX_IDENTITY_USER_ROLE");
        migrationBuilder.DropTable("ZX_IDENTITY_ROLE_PERMISSION");
        migrationBuilder.DropTable("ZX_IDENTITY_ROLE");
        migrationBuilder.DropTable("ZX_IDENTITY_PERMISSION");
    }
}
