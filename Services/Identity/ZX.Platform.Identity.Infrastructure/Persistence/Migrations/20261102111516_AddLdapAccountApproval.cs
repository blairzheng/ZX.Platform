using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZX.Platform.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLdapAccountApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_ZX_IDENTITY_USER_NORMALIZED_ACCOUNT",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                newName: "UX_ZID_USER_ACCOUNT");

            // Oracle 提供程序把"放宽可空"渲染成一段 PL/SQL，其 else 分支缺少 NULL 关键字，
            // 列已可空时会生成非法语句。这里用等价且幂等的显式 DDL 替代，
            // 保证 database update 与导出给 DBA 的脚本都不含无法执行的语句。
            migrationBuilder.Sql(
                """
                declare
                    l_nullable all_tab_columns.nullable%type;
                begin
                    select nullable into l_nullable
                      from all_tab_columns
                     where owner = 'ZX_IDENTITY'
                       and table_name = 'ZX_IDENTITY_USER'
                       and column_name = 'PASSWORD_HASH';
                    if l_nullable = 'N' then
                        execute immediate 'alter table "ZX_IDENTITY"."ZX_IDENTITY_USER" modify ("PASSWORD_HASH" null)';
                    end if;
                end;
                """);

            migrationBuilder.AddColumn<string>(
                name: "APPROVAL_STATUS",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                type: "NVARCHAR2(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Approved");

            migrationBuilder.AddColumn<string>(
                name: "AUTH_TYPE",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                type: "NVARCHAR2(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "LOCAL");

            migrationBuilder.AddColumn<string>(
                name: "EMPLOYEE_NO",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                type: "NVARCHAR2(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LDAP_PROVIDER",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                type: "NVARCHAR2(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LDAP_SUBJECT",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                type: "NVARCHAR2(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "REVIEWED_AT_UTC",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                type: "TIMESTAMP(7) WITH TIME ZONE",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "REVIEWED_BY_USER_ID",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                type: "RAW(16)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "REVIEW_REASON",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                type: "NVARCHAR2(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SECURITY_VERSION",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                type: "NUMBER(19)",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateIndex(
                name: "IX_ZID_USER_TYPE_APPROVAL",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                columns: new[] { "AUTH_TYPE", "APPROVAL_STATUS" });

            migrationBuilder.CreateIndex(
                name: "UX_ZID_USER_EMPLOYEE_NO",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                column: "EMPLOYEE_NO",
                unique: true,
                filter: "\"EMPLOYEE_NO\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_ZID_USER_LDAP_IDENTITY",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                columns: new[] { "LDAP_PROVIDER", "LDAP_SUBJECT" },
                unique: true,
                filter: "\"LDAP_PROVIDER\" IS NOT NULL AND \"LDAP_SUBJECT\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ZID_USER_APPROVAL",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                sql: "APPROVAL_STATUS IN ('Pending', 'Approved', 'Rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ZID_USER_AUTH_TYPE",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                sql: "AUTH_TYPE IN ('LOCAL', 'LDAP')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ZID_USER_LDAP_ENABLED",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                sql: "AUTH_TYPE <> 'LDAP' OR APPROVAL_STATUS = 'Approved' OR IS_ENABLED = 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ZID_USER_PASSWORD",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                sql: "(AUTH_TYPE = 'LDAP' AND PASSWORD_HASH IS NULL) OR (AUTH_TYPE = 'LOCAL' AND PASSWORD_HASH IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ZID_USER_TYPE_APPROVAL",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropIndex(
                name: "UX_ZID_USER_EMPLOYEE_NO",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropIndex(
                name: "UX_ZID_USER_LDAP_IDENTITY",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ZID_USER_APPROVAL",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ZID_USER_AUTH_TYPE",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ZID_USER_LDAP_ENABLED",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ZID_USER_PASSWORD",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "APPROVAL_STATUS",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "AUTH_TYPE",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "EMPLOYEE_NO",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "LDAP_PROVIDER",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "LDAP_SUBJECT",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "REVIEWED_AT_UTC",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "REVIEWED_BY_USER_ID",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "REVIEW_REASON",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.DropColumn(
                name: "SECURITY_VERSION",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER");

            migrationBuilder.RenameIndex(
                name: "UX_ZID_USER_ACCOUNT",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                newName: "IX_ZX_IDENTITY_USER_NORMALIZED_ACCOUNT");

            migrationBuilder.AlterColumn<string>(
                name: "PASSWORD_HASH",
                schema: "ZX_IDENTITY",
                table: "ZX_IDENTITY_USER",
                type: "NVARCHAR2(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "NVARCHAR2(500)",
                oldMaxLength: 500,
                oldNullable: true);
        }
    }
}
