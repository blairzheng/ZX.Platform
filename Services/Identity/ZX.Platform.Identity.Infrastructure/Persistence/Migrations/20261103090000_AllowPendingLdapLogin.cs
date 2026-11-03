using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZX.Platform.Identity.Infrastructure.Persistence.Migrations
{
    /// <summary>分离 OA 登录资格与业务审批；保留所有既有用户的启停用状态和授权。</summary>
    public partial class AllowPendingLdapLogin : Migration
    {
        private const string Schema = "ZX_IDENTITY";
        private const string Table = "ZX_IDENTITY_USER";
        private const string Constraint = "CK_ZID_USER_LDAP_ENABLED";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(Constraint, Table, Schema);
            migrationBuilder.AddCheckConstraint(Constraint, Table,
                "AUTH_TYPE <> 'LDAP' OR APPROVAL_STATUS <> 'Rejected' OR IS_ENABLED = 0", Schema);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Oracle DDL 隐式提交：先阻止不兼容回退，不为恢复旧约束而批量禁用账号。
            migrationBuilder.Sql(
                $"""
                declare
                    v_enabled_pending number;
                begin
                    select count(*) into v_enabled_pending
                      from "{Schema}"."{Table}"
                     where AUTH_TYPE = 'LDAP' and APPROVAL_STATUS = 'Pending' and IS_ENABLED = 1;
                    if v_enabled_pending > 0 then
                        raise_application_error(-20011,
                            'Enabled pending LDAP users exist; review them before reverting the login policy.');
                    end if;
                end;
                """);
            migrationBuilder.DropCheckConstraint(Constraint, Table, Schema);
            migrationBuilder.AddCheckConstraint(Constraint, Table,
                "AUTH_TYPE <> 'LDAP' OR APPROVAL_STATUS = 'Approved' OR IS_ENABLED = 0", Schema);
        }
    }
}
