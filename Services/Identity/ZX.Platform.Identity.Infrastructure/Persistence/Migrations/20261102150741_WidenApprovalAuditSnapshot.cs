using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZX.Platform.Identity.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// 审计快照从 NVARCHAR2(2000) 放宽为 CLOB。
    /// Oracle 不允许把字符列直接 MODIFY 成 CLOB（ORA-22858），
    /// 因此按“新增 CLOB 列 → 复制原值 → 删除旧列 → 改名”执行；
    /// 复制而不是截断，保证既有审计证据原样保留。
    /// </summary>
    public partial class WidenApprovalAuditSnapshot : Migration
    {
        private const string Schema = "ZX_IDENTITY";
        private const string Table = "ZX_IDENTITY_USER_APPROVAL_AUDIT";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            WidenColumn(migrationBuilder, "ROLE_IDS");
            WidenColumn(migrationBuilder, "SBU_IDS");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 回退会重新引入 2000 字符限制：先检查是否真的放得下，放不下就明确失败而不是截断数据。
            migrationBuilder.Sql(
                $"""
                declare
                    v_too_long number;
                begin
                    select count(*) into v_too_long
                      from "{Schema}"."{Table}"
                     where dbms_lob.getlength("ROLE_IDS") > 2000
                        or dbms_lob.getlength("SBU_IDS") > 2000;
                    if v_too_long > 0 then
                        raise_application_error(-20010,
                            'Audit snapshots exceed 2000 characters; archive them before narrowing the columns.');
                    end if;
                end;
                """);

            NarrowColumn(migrationBuilder, "ROLE_IDS");
            NarrowColumn(migrationBuilder, "SBU_IDS");
        }

        private static void WidenColumn(MigrationBuilder migrationBuilder, string column)
        {
            var temporary = column + "_CLOB";
            migrationBuilder.Sql(
                $"alter table \"{Schema}\".\"{Table}\" add (\"{temporary}\" CLOB)");
            // 显式 TO_CLOB：避免 NVARCHAR2 直接赋给 CLOB 时依赖隐式转换的行为差异。
            migrationBuilder.Sql(
                $"update \"{Schema}\".\"{Table}\" set \"{temporary}\" = to_clob(\"{column}\")");
            migrationBuilder.Sql(
                $"alter table \"{Schema}\".\"{Table}\" drop column \"{column}\"");
            migrationBuilder.Sql(
                $"alter table \"{Schema}\".\"{Table}\" rename column \"{temporary}\" to \"{column}\"");
        }

        private static void NarrowColumn(MigrationBuilder migrationBuilder, string column)
        {
            var temporary = column + "_NARROW";
            migrationBuilder.Sql(
                $"alter table \"{Schema}\".\"{Table}\" add (\"{temporary}\" NVARCHAR2(2000))");
            migrationBuilder.Sql(
                $"update \"{Schema}\".\"{Table}\" set \"{temporary}\" = \"{column}\"");
            migrationBuilder.Sql(
                $"alter table \"{Schema}\".\"{Table}\" drop column \"{column}\"");
            migrationBuilder.Sql(
                $"alter table \"{Schema}\".\"{Table}\" rename column \"{temporary}\" to \"{column}\"");
        }
    }
}
