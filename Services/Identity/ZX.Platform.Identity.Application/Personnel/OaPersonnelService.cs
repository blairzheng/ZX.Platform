using System.Diagnostics;
using ZX.Platform.Identity.Application.Authentication;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Application.Personnel.Dtos;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Application.Personnel;

public sealed class OaPersonnelService(
    IOaPersonnelDirectory directory,
    IOaPersonnelUserRepository users,
    IUserApprovalAuditRepository approvalAudits,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IOaPersonnelService
{
    public async Task<OaPersonnelSyncResultDto> SyncAsync(Guid actorId, CancellationToken cancellationToken)
    {
        // 外部查询全部成功才开始登记写入。目录超时、分页截断不能造成半批入库。
        var source = await directory.ReadAsync(cancellationToken);
        var syncedAtUtc = timeProvider.GetUtcNow();
        var businessUnit = directory.BusinessUnit.Trim();
        // 账号和工号是全局唯一；目录身份按来源区分。包含已删除身份，沿用已释放本地账号名规则。
        var byIdentity = new Dictionary<string, HashSet<User>>(StringComparer.OrdinalIgnoreCase);
        foreach (var user in await users.GetExistingAsync(cancellationToken))
            IndexUser(byIdentity, user);
        var added = new List<User>();
        var skippedCount = 0;
        var profileAddedCount = 0;
        var invalidCount = 0;

        foreach (var entry in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryParseDepartment(entry.DepartmentRaw, businessUnit, out var departmentName, out var departmentCode) ||
                !IsValid(entry, businessUnit))
            {
                invalidCount++;
                continue;
            }

            var provider = entry.Provider.Trim();
            var subject = Guid.Parse(entry.Subject).ToString("D");
            var employeeNo = entry.EmployeeNo.Trim();
            var candidates = new HashSet<User>();
            foreach (var key in new[]
            {
                "account\n" + employeeNo,
                "employee\n" + employeeNo,
                "identity\n" + Key(provider, subject),
                "oa-account\n" + Key(provider, entry.Account)
            })
            {
                if (byIdentity.TryGetValue(key, out var matches))
                    candidates.UnionWith(matches);
            }
            if (candidates.Count > 0)
            {
                // 不创建第二个账号、不换绑预开通用户。只给完全相同的已绑定身份补一次组织资料。
                if (candidates.Count == 1)
                {
                    var existing = candidates.Single();
                    if (CanBackfill(existing, provider, subject, employeeNo) &&
                        existing.BackfillOaProfile(
                            entry.Account, entry.Name, entry.DisplayName, entry.Email, entry.Company,
                            businessUnit, departmentName, departmentCode, entry.DepartmentRaw,
                            entry.DistinguishedName, entry.ManagerDn, entry.DirectoryStatus, syncedAtUtc, actorId))
                    {
                        profileAddedCount++;
                        IndexUser(byIdentity, existing);
                    }
                }
                skippedCount++;
                continue;
            }

            // 现有登录姓名列只有 100 字符，拒绝超长源记录，避免截断或整批写库失败。
            if (!Fits(entry.Name, 100, true))
            {
                invalidCount++;
                continue;
            }
            var userToAdd = User.CreateLdapFromDirectory(
                employeeNo, provider, subject, entry.Account, entry.Name, entry.DisplayName,
                entry.Email, entry.Company, businessUnit, departmentName, departmentCode,
                entry.DepartmentRaw, entry.DistinguishedName, entry.ManagerDn, entry.DirectoryStatus,
                syncedAtUtc, actorId);
            added.Add(userToAdd);
            IndexUser(byIdentity, userToAdd);
            approvalAudits.Add(UserApprovalAudit.CreateForNoneStatus(
                userToAdd, UserApprovalActions.Provisioned, actorId, null, [], [],
                Activity.Current?.TraceId.ToHexString()));
        }

        if (added.Count > 0 || profileAddedCount > 0)
        {
            users.AddRange(added);
            try
            {
                // 单次 SaveChanges 的事务及数据库唯一索引兜底并发同步。
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (UniqueConstraintException)
            {
                throw new BusinessException(ErrorCodes.Conflict, "OaPersonnel.SyncConflict");
            }
            catch (ConcurrencyConflictException)
            {
                throw new BusinessException(ErrorCodes.Conflict, "OaPersonnel.SyncConflict");
            }
        }

        var total = (await users.GetAllAsync(businessUnit, cancellationToken)).Count;
        return new OaPersonnelSyncResultDto
        {
            ReadCount = source.Count,
            AddedCount = added.Count,
            SkippedCount = skippedCount,
            ProfileAddedCount = profileAddedCount,
            InvalidCount = invalidCount,
            TotalCount = total,
            SyncedAtUtc = syncedAtUtc
        };
    }

    public async Task<OaPersonnelTreeDto> GetTreeAsync(CancellationToken cancellationToken)
    {
        var businessUnit = directory.BusinessUnit.Trim();
        var records = await users.GetAllAsync(businessUnit, cancellationToken);
        // DN 只用于本次匹配主管；接口不暴露整条目录路径，也不拿姓名猜测上下级。
        var byDn = records.Where(person => !string.IsNullOrWhiteSpace(person.LdapProvider) &&
                !string.IsNullOrWhiteSpace(person.OaDistinguishedName))
            .GroupBy(person => Key(person.LdapProvider!, person.OaDistinguishedName!), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var result = new List<OaPersonDto>(records.Count);
        foreach (var person in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            User? manager = null;
            if (!string.IsNullOrWhiteSpace(person.OaManagerDn) && !string.IsNullOrWhiteSpace(person.LdapProvider))
                byDn.TryGetValue(Key(person.LdapProvider, person.OaManagerDn), out manager);

            result.Add(new OaPersonDto
            {
                Id = person.Id,
                EmployeeNo = person.EmployeeNo!,
                Account = person.Account,
                Name = person.OaName ?? person.DisplayName,
                DisplayName = person.OaDisplayName ?? person.DisplayName,
                Email = person.OaEmail,
                Company = person.OaCompany,
                DepartmentName = person.OaDepartmentName!,
                DepartmentCode = person.OaDepartmentCode,
                DepartmentRaw = person.OaDepartmentRaw,
                ManagerId = manager?.Id,
                ManagerName = manager is null ? null : manager.OaName ?? manager.DisplayName,
                ManagerRelation = string.IsNullOrWhiteSpace(person.OaManagerDn) ? "None"
                    : manager is null ? "Missing" : "Resolved",
                DirectoryStatus = person.OaDirectoryStatus ?? LdapDirectoryStatus.Unknown,
                SyncedAtUtc = person.OaSyncedAtUtc!.Value
            });
        }

        RemoveCycles(result, cancellationToken);
        var ordered = result.OrderBy(person => person.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(person => person.EmployeeNo, StringComparer.OrdinalIgnoreCase)
            .ThenBy(person => person.Id).ToArray();
        return new OaPersonnelTreeDto
        {
            BusinessUnit = businessUnit,
            Total = ordered.Length,
            DepartmentCount = records.Select(person => (person.OaDepartmentCode, person.OaDepartmentName)).Distinct().Count(),
            LastSyncedAtUtc = records.Count == 0 ? null : records.Max(person => person.OaSyncedAtUtc),
            People = ordered
        };
    }

    private static string Key(string provider, string value) => provider.Trim() + "\n" + value.Trim();

    private static void IndexUser(Dictionary<string, HashSet<User>> index, User user)
    {
        Add("account\n" + user.NormalizedAccount);
        if (!string.IsNullOrWhiteSpace(user.EmployeeNo))
            Add("employee\n" + user.EmployeeNo.Trim());
        if (!string.IsNullOrWhiteSpace(user.LdapProvider))
        {
            if (!string.IsNullOrWhiteSpace(user.LdapSubject))
            {
                var subject = Guid.TryParse(user.LdapSubject, out var guid)
                    ? guid.ToString("D") : user.LdapSubject;
                Add("identity\n" + Key(user.LdapProvider, subject));
            }
            if (!string.IsNullOrWhiteSpace(user.OaAccount))
                Add("oa-account\n" + Key(user.LdapProvider, user.OaAccount));
        }

        void Add(string key)
        {
            if (!index.TryGetValue(key, out var matches))
                index[key] = matches = [];
            matches.Add(user);
        }
    }

    private static bool CanBackfill(User user, string provider, string subject, string employeeNo) =>
        user.IsLdap && !user.IsDeleted && !user.OaSyncedAtUtc.HasValue &&
        string.Equals(user.LdapProvider, provider, StringComparison.Ordinal) &&
        string.Equals(user.EmployeeNo, employeeNo, StringComparison.Ordinal) &&
        Guid.TryParse(user.LdapSubject, out var existingSubject) && existingSubject == Guid.Parse(subject);

    private static bool Fits(string? value, int maxLength, bool required = false) =>
        (!required || !string.IsNullOrWhiteSpace(value)) && (value?.Trim().Length ?? 0) <= maxLength &&
        (value is null || !value.Any(char.IsControl));

    private static bool IsValid(OaDirectoryPerson entry, string businessUnit) =>
        Fits(businessUnit, 64, true) && Fits(entry.Provider, 32, true) &&
        Guid.TryParse(entry.Subject, out var subject) && subject != Guid.Empty &&
        Fits(entry.Account, 100, true) && Fits(entry.EmployeeNo, 32, true) &&
        entry.EmployeeNo.IndexOfAny(['\\', '/', '@']) < 0 &&
        Fits(entry.Name, 200, true) && Fits(entry.DisplayName, 256, true) &&
        Fits(entry.Email, 320) && Fits(entry.Company, 256) && Fits(entry.DepartmentRaw, 1000, true) &&
        Fits(entry.DistinguishedName, 2048, true) && Fits(entry.ManagerDn, 2048) &&
        entry.DirectoryStatus is LdapDirectoryStatus.Enabled or LdapDirectoryStatus.Disabled or LdapDirectoryStatus.Unknown;

    private static bool TryParseDepartment(string? raw, string businessUnit, out string name, out string? code)
    {
        name = string.Empty;
        code = null;
        var parts = raw?.Split(',', StringSplitOptions.TrimEntries);
        if (parts is null || parts.Length < 3 ||
            !string.Equals(parts[^1], businessUnit, StringComparison.OrdinalIgnoreCase))
            return false;

        name = string.Join(',', parts[..^2]);
        code = string.IsNullOrWhiteSpace(parts[^2]) ? null : parts[^2];
        return Fits(name, 256, true) && Fits(code, 100);
    }

    private static void RemoveCycles(IReadOnlyList<OaPersonDto> people, CancellationToken cancellationToken)
    {
        var byId = people.ToDictionary(person => person.Id);
        var completed = new HashSet<Guid>();
        foreach (var start in people)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (completed.Contains(start.Id))
                continue;
            var path = new List<OaPersonDto>();
            var positions = new Dictionary<Guid, int>();
            var current = start;
            while (!completed.Contains(current.Id))
            {
                if (positions.TryGetValue(current.Id, out var cycleStart))
                {
                    // 只断开环内的边，保留下属挂接，并在详情明确标注源数据关系异常。
                    foreach (var member in path.Skip(cycleStart))
                    {
                        member.ManagerId = null;
                        member.ManagerRelation = "Cycle";
                    }
                    break;
                }
                positions.Add(current.Id, path.Count);
                path.Add(current);
                if (current.ManagerId is not { } parentId || !byId.TryGetValue(parentId, out var parent))
                    break;
                current = parent;
            }
            foreach (var member in path)
                completed.Add(member.Id);
        }
    }
}
