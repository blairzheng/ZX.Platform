using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZX.Platform.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserApprovalAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ZX_IDENTITY_USER_APPROVAL_AUDIT",
                schema: "ZX_IDENTITY",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    TARGET_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    TARGET_ACCOUNT = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    TARGET_EMPLOYEE_NO = table.Column<string>(type: "NVARCHAR2(32)", maxLength: 32, nullable: false),
                    ACTION = table.Column<string>(type: "NVARCHAR2(32)", maxLength: 32, nullable: false),
                    FROM_STATUS = table.Column<string>(type: "NVARCHAR2(32)", maxLength: 32, nullable: false),
                    TO_STATUS = table.Column<string>(type: "NVARCHAR2(32)", maxLength: 32, nullable: false),
                    ACTOR_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    REASON = table.Column<string>(type: "NVARCHAR2(500)", maxLength: 500, nullable: true),
                    ROLE_IDS = table.Column<string>(type: "NVARCHAR2(2000)", maxLength: 2000, nullable: true),
                    SBU_IDS = table.Column<string>(type: "NVARCHAR2(2000)", maxLength: 2000, nullable: true),
                    TRACE_ID = table.Column<string>(type: "NVARCHAR2(64)", maxLength: 64, nullable: true),
                    CREATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    CREATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    UPDATED_BY_USER_ID = table.Column<Guid>(type: "RAW(16)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZX_IDENTITY_USER_APPROVAL_AUDIT", x => x.ID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ZX_IDENTITY_USER_APPROVAL_AUDIT_CREATED_AT_UTC",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER_APPROVAL_AUDIT",
                column: "CREATED_AT_UTC");

            migrationBuilder.CreateIndex(
                name: "IX_ZX_IDENTITY_USER_APPROVAL_AUDIT_TARGET_USER_ID_CREATED_AT_UTC",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER_APPROVAL_AUDIT",
                columns: new[] { "TARGET_USER_ID", "CREATED_AT_UTC" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ZX_IDENTITY_USER_APPROVAL_AUDIT",
                schema: "ZX_IDENTITY");
        }
    }
}
