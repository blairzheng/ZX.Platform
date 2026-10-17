using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZX.Platform.Identity.Infrastructure.Persistence.Migrations;

public partial class AddMenus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ZX_IDENTITY_MENU",
            columns: table => new
            {
                ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PARENT_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CODE = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                KIND = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                TITLE = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                TITLE_EN = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                ICON = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                PATH = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                COMPONENT = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                PERMISSION_CODE = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                SORT = table.Column<int>(type: "int", nullable: false),
                KEEP_ALIVE = table.Column<bool>(type: "bit", nullable: false),
                IS_ENABLED = table.Column<bool>(type: "bit", nullable: false),
                IS_BUILT_IN = table.Column<bool>(type: "bit", nullable: false),
                IS_DELETED = table.Column<bool>(type: "bit", nullable: false),
                CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CREATED_BY_USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UPDATED_AT_UTC = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                UPDATED_BY_USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_ZX_IDENTITY_MENU", x => x.ID));

        migrationBuilder.CreateIndex("IX_ZX_IDENTITY_MENU_CODE", "ZX_IDENTITY_MENU", "CODE", unique: true);
        migrationBuilder.CreateIndex("IX_ZX_IDENTITY_MENU_PARENT_ID", "ZX_IDENTITY_MENU", "PARENT_ID");
        migrationBuilder.CreateIndex("IX_ZX_IDENTITY_MENU_PATH", "ZX_IDENTITY_MENU", "PATH", unique: true, filter: "[KIND] = 'page' AND [IS_DELETED] = 0");
        migrationBuilder.CreateIndex("IX_ZX_IDENTITY_MENU_PERMISSION_CODE", "ZX_IDENTITY_MENU", "PERMISSION_CODE", unique: true, filter: "[KIND] = 'action' AND [IS_DELETED] = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("ZX_IDENTITY_MENU");
}\n
