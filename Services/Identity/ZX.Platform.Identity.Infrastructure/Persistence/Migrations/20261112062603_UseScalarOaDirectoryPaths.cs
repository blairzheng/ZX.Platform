using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZX.Platform.Identity.Infrastructure.Persistence.Migrations
{
    public partial class UseScalarOaDirectoryPaths : Migration
    {
        private const string Schema = "ZX_IDENTITY";
        private const string Table = "ZX_IDENTITY_USER";
        private const string DistinguishedName = "OA_DISTINGUISHED_NAME";
        private const string ManagerDn = "OA_MANAGER_DN";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 在任何 DDL 前检查两列；不截断旧值，也不把非 NULL 的空 LOB 静默变成 NULL。
            migrationBuilder.Sql(
                $"""
                declare
                    procedure check_dn(p_value nclob) is
                        v_length number;
                        v_text nvarchar2(2000);
                    begin
                        if p_value is null then
                            return;
                        end if;
                        v_length := dbms_lob.getlength(p_value);
                        if v_length = 0 or v_length > 2000 then
                            raise_application_error(-20030,
                                'OA DN must contain 1 to 2000 characters or be NULL; no columns changed.');
                        end if;
                        v_text := dbms_lob.substr(p_value, 2000, 1);
                        if lengthb(v_text) > 4000 then
                            raise_application_error(-20031,
                                'OA DN exceeds the 4000-byte scalar limit; no columns changed.');
                        end if;
                    end;
                begin
                    for person in (select "{DistinguishedName}", "{ManagerDn}" from "{Schema}"."{Table}") loop
                        check_dn(person."{DistinguishedName}");
                        check_dn(person."{ManagerDn}");
                    end loop;
                end;
                """, suppressTransaction: true);

            ConvertColumns(migrationBuilder, toScalar: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ConvertColumns(migrationBuilder, toScalar: false);
        }

        private static void ConvertColumns(MigrationBuilder migrationBuilder, bool toScalar)
        {
            var suffix = toScalar ? "_TEXT" : "_LOB";
            var dnTemporary = DistinguishedName + suffix;
            var managerTemporary = ManagerDn + suffix;
            var targetType = toScalar ? "NVARCHAR2(2000)" : "NCLOB";

            string ConvertValue(string column) => toScalar
                ? $"dbms_lob.substr(\"{column}\", 2000, 1)"
                : $"to_nclob(\"{column}\")";

            string ValuesDiffer(string column, string temporary)
            {
                var lob = toScalar ? column : temporary;
                var scalar = toScalar ? temporary : column;
                return $"""
                    ("{column}" is null and "{temporary}" is not null)
                    or ("{column}" is not null and "{temporary}" is null)
                    or ("{column}" is not null and "{temporary}" is not null
                        and nvl(dbms_lob.compare("{lob}", to_nclob("{scalar}")), 1) <> 0)
                    """;
            }

            // Oracle 不支持直接 MODIFY LOB 为字符串。两列都复制并校验成功后才删除原列。
            // DDL 隐式提交；执行前必须停写，失败后检查现场，不自动重试。
            migrationBuilder.Sql(
                $"""
                declare
                    v_invalid number;
                begin
                    execute immediate 'alter table "{Schema}"."{Table}"
                        add ("{dnTemporary}" {targetType}, "{managerTemporary}" {targetType})';
                    execute immediate 'update "{Schema}"."{Table}"
                        set "{dnTemporary}" = {ConvertValue(DistinguishedName)},
                            "{managerTemporary}" = {ConvertValue(ManagerDn)}';
                    execute immediate 'select count(*) from "{Schema}"."{Table}"
                        where {ValuesDiffer(DistinguishedName, dnTemporary)}
                           or {ValuesDiffer(ManagerDn, managerTemporary)}' into v_invalid;
                    if v_invalid > 0 then
                        raise_application_error(-20032,
                            'OA DN conversion failed verification; original columns retained.');
                    end if;
                    execute immediate 'alter table "{Schema}"."{Table}" drop ("{DistinguishedName}", "{ManagerDn}")';
                    execute immediate 'alter table "{Schema}"."{Table}" rename column "{dnTemporary}" to "{DistinguishedName}"';
                    execute immediate 'alter table "{Schema}"."{Table}" rename column "{managerTemporary}" to "{ManagerDn}"';
                end;
                """, suppressTransaction: true);
        }
    }
}
