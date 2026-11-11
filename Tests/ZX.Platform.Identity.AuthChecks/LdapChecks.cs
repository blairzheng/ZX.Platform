using ZX.Platform.Identity.Application.Authentication;
using ZX.Platform.Identity.Application.Authentication.Dtos;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;
using ZX.Platform.Identity.Application.Users.Dtos;
using ZX.Platform.Identity.Domain.Entities;
using ZX.Platform.Identity.Domain.Exceptions;
using ZX.Platform.Identity.Infrastructure.Security;
using RedisKey = StackExchange.Redis.RedisKey;

namespace ZX.Platform.Identity.AuthChecks;

/// <summary>
/// OA 登录与开通审批的逻辑检查：使用替代目录适配器，不连接真实 LDAP。
/// 真实目录特性、容器原生依赖和浏览器联调不在这里覆盖。
/// </summary>
internal static class LdapChecks
{
    private const string EmployeeNo = "10001";
    private const string OaPassword = "Oa#12345";

    public static async Task RunAsync()
    {
        await FirstLoginCreatesPendingAsync();
        await RepeatLoginKeepsPendingAsync();
        await PendingCannotUsePreassignedGrantsAsync();
        await DisabledPendingCannotBeReenabledByLoginOrApprovalAsync();
        await ApprovalEnablesLoginAsync();
        await RejectionAndDisableBlockLoginAsync();
        await AccountConflictIsRejectedAsync();
        await DeletedLocalAccountCreatesNewPendingAsync();
        await WrongPasswordDoesNotReleaseDeletedLocalAsync();
        LocalAccountNameReleaseIsBoundedAndIdempotent();
        await LocalAccountCanBeRestoredBeforeReuseAsync();
        await ReusedLocalAccountCannotBeRestoredAsync();
        await ProvisionReleasesHistoricalLocalAccountAsync();
        await DeletedAccountIsNotRebuiltAsync();
        await ProvisionImmediatelyCreatesApprovedUserAsync();
        await StaleVersionIsRejectedAsync();
        await ApprovedAccountCannotBeReapprovedAsync();
        await LocalLoginDoesNotLockOaAccountAsync();
        await ApprovalWritesAuditTrailAsync();
        await DirectoryLoginNameIsNotTheZxAccountAsync();
        await LimiterIsHeldUntilWorkFinishesAsync();
        await GuardRepairsKeyWithoutExpiryAsync();
        SearchFailuresAreClassified();
        await DependencyFailuresAreControlledAsync();
        await ProvisionWorksWithoutDirectoryAsync();
        await ProvisionedIdentityRequiresPasswordAndCannotBeReboundAsync();
        await UnavailableProvisionedAccountsStayUnboundAsync();
        Console.WriteLine("LDAP 账号开通检查通过。");
    }

    private static async Task FirstLoginCreatesPendingAsync()
    {
        using var fixture = CreateFixture();
        var result = await fixture.Auth.LdapLoginAsync(Login(), default);

        Check.That(result.Status == LdapLoginStatus.Authenticated, "首次 OA 认证即应登录成功。");
        Check.That(result.Created == true, "首次 OA 认证应标记为新建。");
        Check.That(result.Tokens is not null, "待业务审批也应签发会话令牌。");

        var user = SingleLdapUser(fixture);
        Check.That(user.IsEnabled && user.ApprovalStatus == UserApprovalStatus.Pending, "新 OA 账号应允许登录且待业务审批。");
        Check.That(user.PasswordHash is null, "OA 账号不能有本地密码哈希。");
        Check.That(user.AuthType == UserAuthType.Ldap, "认证类型应为 LDAP。");
        Check.That(!fixture.Roles.UserRoles.Any(item => item.UserId == user.Id), "待审批账号不能分配角色。");
        Check.That(!fixture.UserSbus.Items.Any(item => item.UserId == user.Id), "待审批账号不能分配 SBU。");

        var current = await fixture.Auth.GetCurrentUserAsync(user.Id, default);
        Check.That(!current.HasBusinessAccess && current.ApprovalStatus == UserApprovalStatus.Pending &&
            !current.IsAdministrator && current.Roles.Length == 0 && current.Permissions.Length == 0 &&
            current.Sbus.Count == 0 && current.Menus.Count == 0,
            "待授权会话只能读取自己的基本信息，不能得到业务能力。");
        var refreshed = await fixture.Auth.RefreshAsync(result.Tokens!.RefreshToken, default);
        Check.That(await fixture.FindAccessAsync(refreshed.AccessToken, default) is not null,
            "待授权用户应可刷新并维持有效登录。");

        // OA 账号不能改用本地密码入口登录。
        await Check.ErrorAsync(
            () => fixture.Auth.LoginAsync(new LoginDto { Account = EmployeeNo, Password = OaPassword }, default),
            ErrorCodes.Unauthorized);
    }

    private static async Task RepeatLoginKeepsPendingAsync()
    {
        using var fixture = CreateFixture();
        var first = await fixture.Auth.LdapLoginAsync(Login(), default);
        var user = SingleLdapUser(fixture);
        var version = user.SecurityVersion;

        await Check.ErrorAsync(() => fixture.Auth.LdapLoginAsync(Login(), default), ErrorCodes.Conflict);
        var takeover = Login();
        takeover.ReplaceExistingSession = true;
        var second = await fixture.Auth.LdapLoginAsync(takeover, default);
        Check.That(second.Status == LdapLoginStatus.Authenticated, "待业务审批用户可重复认证并接管会话。");
        Check.That(second.Created == false, "重复登录不能重复创建账号。");
        Check.That(second.Tokens is not null, "待业务审批也应签发令牌。");
        Check.That(await fixture.FindAccessAsync(first.Tokens!.AccessToken, default) is null,
            "待授权会话也必须遵守单会话接管规则。");
        Check.That(fixture.Users.Items.Count(item => item.IsLdap) == 1, "重复登录只能有一条 OA 账号。");
        Check.That(user.SecurityVersion == version, "待审批重复登录不能改变安全版本。");
        Check.That(first.Created == true, "第一次登录应创建账号。");
    }

    private static async Task PendingCannotUsePreassignedGrantsAsync()
    {
        using var fixture = CreateFixture();
        await fixture.Auth.LdapLoginAsync(Login(), default);
        var user = SingleLdapUser(fixture);
        var administrator = fixture.Roles.Items.Single(role => role.IsBuiltIn);
        var (_, sbu) = SeedGrants(fixture);
        fixture.Roles.AddUserRole(UserRole.Create(user.Id, administrator.Id));
        fixture.UserSbus.Add(UserSbu.Create(user.Id, sbu.Id, false));
        fixture.UserSbus.Sbus.Add(sbu);
        var cached = await fixture.PermissionCache.ReadAsync(user.Id, default);
        await fixture.PermissionCache.StoreAsync(user.Id, cached.Version,
            new AuthorizationSnapshot(true, [Role.AdministratorCode], IdentityPermissions.All.ToArray()), default);

        var permissions = new PermissionService(fixture.Roles, fixture.PermissionCache, fixture.Users);
        Check.That(!await permissions.HasAsync(user.Id, IdentityPermissions.UserRead, default),
            "待审批账号的预分配管理员角色和旧缓存均不得授权。");
        var current = await fixture.Auth.GetCurrentUserAsync(user.Id, default);
        Check.That(!current.IsAdministrator && current.Permissions.Length == 0 && current.Menus.Count == 0 &&
            current.Roles.Length == 0 && current.Sbus.Count == 0, "me 必须强制屏蔽预分配的所有关联。");
        Check.That(await fixture.UserSbus.GetEnabledSbuForUserAsync(user.Id, sbu.Id, default) is null,
            "伪造 X-SBU-ID 也不能使用待审批账号的预分配 SBU。");
    }

    private static async Task DisabledPendingCannotBeReenabledByLoginOrApprovalAsync()
    {
        using var fixture = CreateFixture();
        var initial = await fixture.Auth.LdapLoginAsync(Login(), default);
        var user = SingleLdapUser(fixture);
        await fixture.UserService.SetUserDisableAsync(user.Id, default);
        var version = user.SecurityVersion;
        await Check.ErrorAsync(() => fixture.Auth.LdapLoginAsync(Login(), default), ErrorCodes.Forbidden);
        await Check.ErrorAsync(() => fixture.Auth.RefreshAsync(initial.Tokens!.RefreshToken, default), ErrorCodes.Unauthorized);
        await Check.ErrorAsync(() => fixture.Auth.GetCurrentUserAsync(user.Id, default), ErrorCodes.Unauthorized);
        Check.That(!user.IsEnabled && user.SecurityVersion == version, "重新 OA 认证不得恢复禁用账号。");

        // 与历史 Pending/Disabled 用户相同：迁移或审批不得推断禁用原因并恢复启用。
        var (role, sbu) = SeedGrants(fixture);
        await fixture.UserService.ApproveLdapAsync(fixture.User.Id, new ApproveLdapUserDto
        {
            Id = user.Id, RoleIds = [role.Id], SbuIds = [sbu.Id], ExpectedSecurityVersion = version
        }, default);
        Check.That(!user.IsEnabled && user.ApprovalStatus == UserApprovalStatus.Approved,
            "审批不能顺带恢复被禁用的待审批用户。");
        await Check.ErrorAsync(() => fixture.Auth.LdapLoginAsync(Login(), default), ErrorCodes.Forbidden);
        await fixture.UserService.SetUserEnableAsync(user.Id, default);
        Check.That((await fixture.Auth.LdapLoginAsync(Login(), default)).Tokens is not null,
            "显式启用后方可恢复登录。");
    }

    private static async Task ApprovalEnablesLoginAsync()
    {
        using var fixture = CreateFixture();
        var initial = await fixture.Auth.LdapLoginAsync(Login(), default);
        var user = SingleLdapUser(fixture);
        var (role, sbu) = SeedGrants(fixture);
        var versionBefore = user.SecurityVersion;

        await fixture.UserService.ApproveLdapAsync(fixture.User.Id, new ApproveLdapUserDto
        {
            Id = user.Id,
            RoleIds = [role.Id],
            SbuIds = [sbu.Id],
            ExpectedSecurityVersion = user.SecurityVersion,
            Reason = "预开通"
        }, default);

        Check.That(user.ApprovalStatus == UserApprovalStatus.Approved, "审批后应为已通过。");
        Check.That(user.IsEnabled, "审批后应启用。");
        Check.That(user.SecurityVersion > versionBefore, "审批必须递增安全版本。");
        Check.That(fixture.Roles.UserRoles.Any(item => item.UserId == user.Id && item.RoleId == role.Id), "审批应写入角色。");
        Check.That(fixture.UserSbus.Items.Any(item => item.UserId == user.Id && item.SbuId == sbu.Id), "审批应写入 SBU。");
        Check.That(await fixture.FindAccessAsync(initial.Tokens!.AccessToken, default) is null,
            "业务审批通过必须撤销待授权会话。");
        await Check.ErrorAsync(() => fixture.Auth.RefreshAsync(initial.Tokens.RefreshToken, default), ErrorCodes.Unauthorized);

        var login = await fixture.Auth.LdapLoginAsync(Login(), default);
        Check.That(login.Status == LdapLoginStatus.Authenticated, "已开通账号应能登录。");
        Check.That(login.Tokens is not null, "登录成功应签发令牌。");
        Check.That(SingleLdapUser(fixture).Id == user.Id, "登录必须沿用同一个 UserId。");

        // OA 账号的密码由目录维护，ZX 拒绝本地改密。
        await Check.ErrorAsync(
            () => fixture.Auth.ChangePasswordAsync(user.Id,
                new ChangePasswordDto { CurrentPassword = OaPassword, NewPassword = "Another123!" }, default),
            ErrorCodes.Forbidden);

        var current = await fixture.Auth.GetCurrentUserAsync(user.Id, default);
        Check.That(current.AuthType == UserAuthType.Ldap && !current.CanChangePassword, "当前用户应标记为不可改密。");
        Check.That(current.HasBusinessAccess && current.Permissions.Contains(IdentityPermissions.UserRead) &&
            current.Sbus.Any(item => item.Id == sbu.Id), "审批后的新会话应得到明确分配的业务权限和 SBU。");
    }

    private static async Task RejectionAndDisableBlockLoginAsync()
    {
        using var fixture = CreateFixture();
        var initial = await fixture.Auth.LdapLoginAsync(Login(), default);
        var user = SingleLdapUser(fixture);

        await fixture.UserService.RejectLdapAsync(fixture.User.Id, new RejectLdapUserDto
        {
            Id = user.Id,
            ExpectedSecurityVersion = user.SecurityVersion,
            Reason = "工号不属于本部门"
        }, default);
        Check.That(user.ApprovalStatus == UserApprovalStatus.Rejected && !user.IsEnabled, "拒绝后应保持禁用。");
        Check.That(await fixture.FindAccessAsync(initial.Tokens!.AccessToken, default) is null,
            "拒绝必须立即撤销待授权会话。");
        await Check.ErrorAsync(() => fixture.Auth.RefreshAsync(initial.Tokens.RefreshToken, default), ErrorCodes.Unauthorized);

        await Check.ErrorAsync(() => fixture.Auth.LdapLoginAsync(Login(), default), ErrorCodes.Forbidden);

        // 已拒绝的账号不能通过启用接口绕过审批；领域异常由 API 映射为 409。
        var enable = await CaptureAsync(() => fixture.UserService.SetUserEnableAsync(user.Id, default));
        Check.That(enable is DomainException or BusinessException { Code: ErrorCodes.InvalidState },
            "已拒绝的账号不能通过启用接口绕过审批。");

        // 审核人显式重新审批后可以正常登录。
        var (role, sbu) = SeedGrants(fixture);
        await fixture.UserService.ApproveLdapAsync(fixture.User.Id, new ApproveLdapUserDto
        {
            Id = user.Id,
            RoleIds = [role.Id],
            SbuIds = [sbu.Id],
            ExpectedSecurityVersion = user.SecurityVersion
        }, default);
        Check.That((await fixture.Auth.LdapLoginAsync(Login(), default)).Status == LdapLoginStatus.Authenticated,
            "重新审批后应能登录。");

        await fixture.UserService.SetUserDisableAsync(user.Id, default);
        await Check.ErrorAsync(() => fixture.Auth.LdapLoginAsync(Login(), default), ErrorCodes.Forbidden);
    }

    private static async Task AccountConflictIsRejectedAsync()
    {
        foreach (var disabled in new[] { false, true })
        {
            using var fixture = CreateFixture();
            // 未删除的本地账号即使禁用，仍不能被 OA 登录自动转换或占用。
            var local = User.CreateLocal(EmployeeNo, "Local Owner", fixture.Passwords.Hash("Example123!"));
            if (disabled)
                local.Disable();
            fixture.Users.Add(local);
            var version = local.SecurityVersion;

            var error = await CaptureAsync(() => fixture.Auth.LdapLoginAsync(Login(), default));
            Check.That(error is BusinessException
                { Code: ErrorCodes.Conflict, MessageKey: "User.AccountTypeConflict" }, "同工号本地账号应返回类型冲突。");
            Check.That(!fixture.Users.Items.Any(item => item.IsLdap), "冲突时不能创建 OA 账号。");
            Check.That(local.AuthType == UserAuthType.Local && local.PasswordHash is not null &&
                local.NormalizedAccount == EmployeeNo && local.SecurityVersion == version &&
                local.IsEnabled == !disabled, "未删除的本地账号和禁用状态必须保持原样。");
        }
    }

    private static async Task DeletedLocalAccountCreatesNewPendingAsync()
    {
        foreach (var historical in new[] { false, true })
        {
            using var fixture = CreateFixture();
            var local = User.CreateLocal(EmployeeNo, "Local Owner", fixture.Passwords.Hash("Example123!"));
            fixture.Users.Add(local);
            var (role, sbu) = SeedGrants(fixture);
            fixture.Roles.AddUserRole(UserRole.Create(local.Id, role.Id));
            fixture.UserSbus.Add(UserSbu.Create(local.Id, sbu.Id, false));
            var oldLogin = await fixture.Auth.LoginAsync(
                new LoginDto { Account = EmployeeNo, Password = "Example123!" }, default);
            await fixture.UserService.SoftDeleteAsync(local.Id, default);
            if (historical)
                KeepHistoricalDeletedAccountName(local);
            var oldId = local.Id;
            var oldHash = local.PasswordHash;
            var saves = 0;
            fixture.Unit.OnSave = () =>
            {
                if (historical && ++saves == 1)
                    Check.That(local.NormalizedAccount != EmployeeNo &&
                        !fixture.Users.Items.Any(item => item.IsLdap),
                        "历史账号必须先释放并保存，再登记同名 OA 用户。");
                return Task.CompletedTask;
            };

            var result = await fixture.Auth.LdapLoginAsync(Login(), default);
            var user = SingleLdapUser(fixture);
            Check.That(result.Created && result.Tokens is not null && user.Id != oldId &&
                user.Account == EmployeeNo && user.ApprovalStatus == UserApprovalStatus.Pending &&
                user.IsEnabled && user.PasswordHash is null, "已删除本地账号应让出名称，OA 创建独立的待审批身份。");
            Check.That(local.Id == oldId && local.Account == EmployeeNo && local.PasswordHash == oldHash &&
                local.IsDeleted && !local.IsEnabled && local.NormalizedAccount != EmployeeNo,
                "旧本地身份、账号和密码哈希必须保留，不能恢复或改成 OA。");
            Check.That(!fixture.Roles.UserRoles.Any(item => item.UserId == user.Id) &&
                !fixture.UserSbus.Items.Any(item => item.UserId == user.Id) &&
                fixture.Roles.UserRoles.Any(item => item.UserId == oldId && item.RoleId == role.Id) &&
                fixture.UserSbus.Items.Any(item => item.UserId == oldId && item.SbuId == sbu.Id),
                "旧角色和 SBU 留在历史身份，新 OA 用户不能继承任何授权。");
            var current = await fixture.Auth.GetCurrentUserAsync(user.Id, default);
            Check.That(!current.HasBusinessAccess && current.Roles.Length == 0 &&
                current.Permissions.Length == 0 && current.Sbus.Count == 0 && current.Menus.Count == 0,
                "新 OA 会话必须等待重新审批，不能访问原有业务。");
            Check.That(await fixture.FindAccessAsync(oldLogin.AccessToken, default) is null,
                "新 OA 登录不能复活旧本地身份的会话。");
        }
    }

    private static async Task WrongPasswordDoesNotReleaseDeletedLocalAsync()
    {
        using var fixture = CreateFixture();
        var local = User.CreateLocal(EmployeeNo, "Local Owner", fixture.Passwords.Hash("Example123!"));
        fixture.Users.Add(local);
        local.MarkDeleted();
        KeepHistoricalDeletedAccountName(local);
        var version = local.SecurityVersion;

        await Check.ErrorAsync(() => fixture.Auth.LdapLoginAsync(
            new LdapLoginDto { EmployeeNo = EmployeeNo, Password = "wrong" }, default), ErrorCodes.Unauthorized);

        Check.That(local.IsDeleted && local.NormalizedAccount == EmployeeNo && local.SecurityVersion == version &&
            !fixture.Users.Items.Any(item => item.IsLdap), "OA 密码错误不能释放历史账号名或创建新身份。");
    }

    private static void LocalAccountNameReleaseIsBoundedAndIdempotent()
    {
        var account = new string('a', 100);
        var local = User.CreateLocal(account, "Local Owner", "local-hash");
        Check.That(!local.ReleaseDeletedLocalAccountName() && local.NormalizedAccount == account.ToUpperInvariant(),
            "未删除的本地账号不允许释放名称。");
        local.MarkDeleted();
        var version = local.SecurityVersion;
        var releasedName = local.NormalizedAccount;
        Check.That(local.Account == account && releasedName == $"DELETED:LOCAL:{local.Id:N}".ToUpperInvariant() &&
            releasedName.Length <= 100, "最长账号应保留原名，内部占位名必须规范化且不超过列长度。");
        Check.That(!local.ReleaseDeletedLocalAccountName() && local.SecurityVersion == version &&
            local.NormalizedAccount == releasedName, "重复释放必须幂等，不再修改安全版本。");
        local.MarkDeleted();
        Check.That(local.SecurityVersion == version, "重复删除不能重新释放或递增安全版本。");

        var oa = User.CreateLdap(EmployeeNo, EmployeeNo, "OA Owner", "test-ad", Guid.NewGuid().ToString("D"));
        oa.MarkDeleted();
        var oaVersion = oa.SecurityVersion;
        Check.That(!oa.ReleaseDeletedLocalAccountName() && oa.NormalizedAccount == EmployeeNo &&
            oa.EmployeeNo == EmployeeNo && oa.SecurityVersion == oaVersion,
            "已删除 OA 账号必须继续保留账号名和工号占位。");
    }

    private static async Task LocalAccountCanBeRestoredBeforeReuseAsync()
    {
        using var fixture = CreateFixture();
        var local = User.CreateLocal("local01", "Local Owner", fixture.Passwords.Hash("Example123!"));
        fixture.Users.Add(local);
        await fixture.UserService.SoftDeleteAsync(local.Id, default);
        Check.That(local.NormalizedAccount != "LOCAL01", "删除应先释放本地账号名。");
        await fixture.UserService.RestoreAsync(local.Id, default);

        Check.That(!local.IsDeleted && local.IsEnabled && local.NormalizedAccount == "LOCAL01" &&
            local.Account == "local01" && local.AuthType == UserAuthType.Local,
            "原账号名未被占用时，恢复应还原同一用户的本地登录身份。");
        var login = await fixture.Auth.LoginAsync(
            new LoginDto { Account = "LOCAL01", Password = "Example123!" }, default);
        Check.That(login is not null, "恢复后原本地密码应可登录。");
    }

    private static async Task ReusedLocalAccountCannotBeRestoredAsync()
    {
        foreach (var reusedByOa in new[] { false, true })
        {
            using var fixture = CreateFixture();
            var local = User.CreateLocal(EmployeeNo, "Local Owner", fixture.Passwords.Hash("Example123!"));
            fixture.Users.Add(local);
            await fixture.UserService.SoftDeleteAsync(local.Id, default);
            if (reusedByOa)
                await fixture.Auth.LdapLoginAsync(Login(), default);
            else
                fixture.Users.Add(User.CreateLocal(EmployeeNo, "New Local Owner", fixture.Passwords.Hash("Another123!")));
            var version = local.SecurityVersion;
            var releasedName = local.NormalizedAccount;

            var error = await CaptureAsync(() => fixture.UserService.RestoreAsync(local.Id, default));
            Check.That(error is BusinessException
                { Code: ErrorCodes.Conflict, MessageKey: "User.AccountReusedCannotRestore" },
                "原账号被新 OA 或本地用户占用时，恢复应返回明确的账号占用提示。");
            Check.That(local.IsDeleted && !local.IsEnabled && local.SecurityVersion == version &&
                local.NormalizedAccount == releasedName, "恢复冲突不能修改旧用户的状态或内部占位名。");
            Check.That(fixture.Users.Items.Count(item => item.NormalizedAccount == EmployeeNo) == 1,
                "恢复冲突不能影响当前占用该名称的新用户。");
        }
    }

    private static async Task ProvisionReleasesHistoricalLocalAccountAsync()
    {
        using var fixture = CreateFixture();
        var local = User.CreateLocal(EmployeeNo, "Local Owner", fixture.Passwords.Hash("Example123!"));
        fixture.Users.Add(local);
        local.MarkDeleted();
        KeepHistoricalDeletedAccountName(local);
        var (role, sbu) = SeedGrants(fixture);
        fixture.Roles.AddUserRole(UserRole.Create(local.Id, role.Id));
        fixture.UserSbus.Add(UserSbu.Create(local.Id, sbu.Id, false));
        fixture.Ldap.IsEnabled = false;
        fixture.Ldap.Unavailable = true;

        var created = await fixture.UserService.CreateLdapAsync(fixture.User.Id,
            new CreateLdapUserDto { EmployeeNo = EmployeeNo }, default);
        var user = SingleLdapUser(fixture);
        Check.That(created.Id == user.Id && user.Id != local.Id && user.ApprovalStatus == UserApprovalStatus.Pending &&
            user.LdapProvider is null && user.LdapSubject is null && fixture.Ldap.AuthenticateCalls == 0,
            "管理员预开通应兼容历史已删本地账号，不访问目录、不合并身份。");
        Check.That(local.IsDeleted && local.Account == EmployeeNo && local.NormalizedAccount != EmployeeNo &&
            !fixture.Roles.UserRoles.Any(item => item.UserId == user.Id) &&
            !fixture.UserSbus.Items.Any(item => item.UserId == user.Id),
            "预开通必须释放旧账号名且不能继承旧角色或 SBU。");
        Check.That(fixture.ApprovalAudits.Items.Single().TargetUserId == user.Id,
            "预开通审计应记录新 OA 身份。");
    }

    private static async Task DeletedAccountIsNotRebuiltAsync()
    {
        using var fixture = CreateFixture();
        await fixture.Auth.LdapLoginAsync(Login(), default);
        var user = SingleLdapUser(fixture);
        user.MarkDeleted();

        await Check.ErrorAsync(() => fixture.Auth.LdapLoginAsync(Login(), default), ErrorCodes.Forbidden);
        Check.That(fixture.Users.Items.Count(item => item.IsLdap) == 1, "已删除工号不能被重新建档。");
        Check.That(user.ApprovalStatus == UserApprovalStatus.Pending, "已删除账号不能自动审批。");
    }

    private static async Task ProvisionImmediatelyCreatesApprovedUserAsync()
    {
        using var fixture = CreateFixture();
        var (role, sbu) = SeedGrants(fixture);

        var created = await fixture.UserService.CreateLdapAsync(fixture.User.Id, new CreateLdapUserDto
        {
            EmployeeNo = EmployeeNo,
            OpenImmediately = true,
            RoleIds = [role.Id],
            SbuIds = [sbu.Id],
            Reason = "负责人预开通"
        }, default);

        Check.That(created.ApprovalStatus == UserApprovalStatus.Approved && created.IsEnabled, "一次创建应直接开通。");
        var provisioned = fixture.Users.Items.Single(item => item.Id == created.Id);
        Check.That(provisioned.PasswordHash is null, "预开通不能生成密码。");
        Check.That(provisioned.LdapProvider is null && provisioned.LdapSubject is null,
            "预开通不得伪造目录来源和身份。");
        var version = provisioned.SecurityVersion;

        var login = await fixture.Auth.LdapLoginAsync(Login(), default);
        Check.That(login.Status == LdapLoginStatus.Authenticated, "预开通用户首次登录应直接进入。");
        Check.That(SingleLdapUser(fixture).Id == created.Id && !login.Created,
            "预开通与首次登录必须使用同一个 UserId，不新建账号。");
        Check.That(provisioned.LdapSubject == fixture.Ldap.Entries[0].Subject &&
            provisioned.DisplayName == "张三" && provisioned.SecurityVersion == version + 1,
            "首次认证后补齐身份与姓名，并更新安全版本。");
        Check.That((await fixture.Roles.GetUserRolesAsync(created.Id, default)).Any(item => item.RoleId == role.Id) &&
            (await fixture.UserSbus.GetByUserIdAsync(created.Id, default)).Any(item => item.SbuId == sbu.Id),
            "首次身份绑定必须保留预先分配的角色和 SBU。");

        // 仅创建待审批时不能同时授权。
        await Check.ErrorAsync(() => fixture.UserService.CreateLdapAsync(fixture.User.Id, new CreateLdapUserDto
        {
            EmployeeNo = "10002",
            OpenImmediately = false,
            RoleIds = [role.Id]
        }, default), ErrorCodes.ValidationFailed);

        using var pendingFixture = CreateFixture();
        var pending = await pendingFixture.UserService.CreateLdapAsync(pendingFixture.User.Id,
            new CreateLdapUserDto { EmployeeNo = EmployeeNo, OpenImmediately = false }, default);
        Check.That(pending.IsEnabled && pending.ApprovalStatus == UserApprovalStatus.Pending,
            "管理员仅创建待授权账号时，也允许员工用 OA 登录。");
        Check.That((await pendingFixture.Auth.LdapLoginAsync(Login(), default)).Tokens is not null,
            "预创建的待授权账号应能建立受限会话。");
    }

    private static async Task StaleVersionIsRejectedAsync()
    {
        using var fixture = CreateFixture();
        await fixture.Auth.LdapLoginAsync(Login(), default);
        var user = SingleLdapUser(fixture);
        var (role, sbu) = SeedGrants(fixture);

        await Check.ErrorAsync(() => fixture.UserService.ApproveLdapAsync(fixture.User.Id, new ApproveLdapUserDto
        {
            Id = user.Id,
            RoleIds = [role.Id],
            SbuIds = [sbu.Id],
            ExpectedSecurityVersion = user.SecurityVersion - 1
        }, default), ErrorCodes.InvalidState);

        Check.That(user.ApprovalStatus == UserApprovalStatus.Pending, "版本不一致时不能审批。");
    }

    /// <summary>
    /// 审批不能当作重新启用的后门：已通过但被禁用的账号必须走启用接口。
    /// 否则只有 approve + 角色 + SBU 权限的人可以绕过 identity.user.status。
    /// </summary>
    private static async Task ApprovedAccountCannotBeReapprovedAsync()
    {
        using var fixture = CreateFixture();
        await fixture.Auth.LdapLoginAsync(Login(), default);
        var user = SingleLdapUser(fixture);
        var (role, sbu) = SeedGrants(fixture);

        await fixture.UserService.ApproveLdapAsync(fixture.User.Id, new ApproveLdapUserDto
        {
            Id = user.Id,
            RoleIds = [role.Id],
            SbuIds = [sbu.Id],
            ExpectedSecurityVersion = user.SecurityVersion
        }, default);
        await fixture.UserService.SetUserDisableAsync(user.Id, default);
        Check.That(!user.IsEnabled, "禁用后应停用。");
        var version = user.SecurityVersion;

        var error = await CaptureAsync(() => fixture.UserService.ApproveLdapAsync(fixture.User.Id,
            new ApproveLdapUserDto
            {
                Id = user.Id,
                RoleIds = [role.Id],
                SbuIds = [sbu.Id],
                ExpectedSecurityVersion = user.SecurityVersion
            }, default));
        Check.That(error is BusinessException { Code: ErrorCodes.InvalidState },
            "已通过的账号不能再次审批。");
        Check.That(!user.IsEnabled && user.ApprovalStatus == UserApprovalStatus.Approved,
            "审批被拒后账号必须保持禁用。");
        Check.That(user.SecurityVersion == version, "被拒绝的审批不能改变安全版本。");

        // 领域层同样兜底，避免其他调用方绕过服务层检查。
        var domain = await CaptureAsync(() =>
        {
            user.ApproveLdap(fixture.User.Id, fixture.Clock.GetUtcNow(), null);
            return Task.CompletedTask;
        });
        Check.That(domain is DomainException, "领域方法必须拒绝已通过账号的再次审批。");
    }

    /// <summary>
    /// 本地登录入口不能把 OA 账号锁住：OA 账号没有本地密码，
    /// 若该入口继续累计失败次数，知道工号就能让正常 OA 登录被本地锁定拦住。
    /// </summary>
    private static async Task LocalLoginDoesNotLockOaAccountAsync()
    {
        using var fixture = CreateFixture();
        await fixture.Auth.LdapLoginAsync(Login(), default);
        var user = SingleLdapUser(fixture);
        var (role, sbu) = SeedGrants(fixture);
        await fixture.UserService.ApproveLdapAsync(fixture.User.Id, new ApproveLdapUserDto
        {
            Id = user.Id,
            RoleIds = [role.Id],
            SbuIds = [sbu.Id],
            ExpectedSecurityVersion = user.SecurityVersion
        }, default);

        var failures = user.AccessFailedCount;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            await Check.ErrorAsync(
                () => fixture.Auth.LoginAsync(new LoginDto { Account = EmployeeNo, Password = "guess" }, default),
                ErrorCodes.Unauthorized);
        }

        Check.That(user.AccessFailedCount == failures, "本地入口不能累计 OA 账号的失败次数。");
        Check.That(!user.IsLocked(fixture.Clock.GetUtcNow()), "本地入口不能让 OA 账号进入锁定期。");
        Check.That((await fixture.Auth.LdapLoginAsync(Login(), default)).Status == LdapLoginStatus.Authenticated,
            "OA 账号仍应能用目录密码登录。");
    }

    /// <summary>
    /// 审批必须留痕：用户表只保存最近一次审核人，重新审核会覆盖旧值。
    /// </summary>
    private static async Task ApprovalWritesAuditTrailAsync()
    {
        using var fixture = CreateFixture();
        var (role, sbu) = SeedGrants(fixture);

        var created = await fixture.UserService.CreateLdapAsync(fixture.User.Id, new CreateLdapUserDto
        {
            EmployeeNo = EmployeeNo,
            OpenImmediately = false
        }, default);
        var user = fixture.Users.Items.Single(item => item.Id == created.Id);

        await fixture.UserService.RejectLdapAsync(fixture.User.Id, new RejectLdapUserDto
        {
            Id = user.Id,
            ExpectedSecurityVersion = user.SecurityVersion,
            Reason = "工号待确认"
        }, default);

        await fixture.UserService.ApproveLdapAsync(fixture.User.Id, new ApproveLdapUserDto
        {
            Id = user.Id,
            RoleIds = [role.Id],
            SbuIds = [sbu.Id],
            ExpectedSecurityVersion = user.SecurityVersion,
            Reason = "确认后开通"
        }, default);

        var trail = fixture.ApprovalAudits.Items
            .Where(item => item.TargetUserId == user.Id)
            .OrderBy(item => item.CreatedAtUtc)
            .ToList();

        Check.That(trail.Count == 3, "预开通、拒绝、重新审核应各写一条审计。");
        Check.That(trail[0].Action == UserApprovalActions.Provisioned &&
            trail[0].FromStatus == "None" && trail[0].ToStatus == UserApprovalStatus.Pending,
            "仅创建待审批时必须记录为 None -> Pending。");
        Check.That(trail[1].Action == UserApprovalActions.Rejected &&
            trail[1].FromStatus == UserApprovalStatus.Pending &&
            trail[1].ToStatus == UserApprovalStatus.Rejected,
            "拒绝必须记录审批前后的状态。");
        Check.That(trail[2].Action == UserApprovalActions.ReApproved &&
            trail[2].FromStatus == UserApprovalStatus.Rejected &&
            trail[2].ToStatus == UserApprovalStatus.Approved,
            "被拒绝后的通过必须标记为重新审核。");
        Check.That(trail.All(item => item.ActorUserId == fixture.User.Id), "审计必须记录操作人。");
        Check.That(trail[2].Reason == "确认后开通", "审计必须保留审核原因。");
        Check.That(trail[2].RoleIds is not null && trail[2].SbuIds is not null,
            "审批通过必须记录本次分配的角色与 SBU 快照。");
        Check.That(trail[0].TargetEmployeeNo == EmployeeNo, "审计必须记录目标工号。");

        var history = await fixture.ApprovalAudits.GetByTargetUserIdAsync(user.Id, default);
        Check.That(history.Count == 3, "审批历史应按目标账号可查。");
    }

    /// <summary>
    /// ZX 账号必须取工号，而不是目录登录名：两者不同时，冲突检查也按工号执行。
    /// 否则“本地账号占用了该工号”永远查不出来，账号定义也会随目录登录名漂移。
    /// </summary>
    private static async Task DirectoryLoginNameIsNotTheZxAccountAsync()
    {
        using (var fixture = CreateFixture(account: "zhangsan"))
        {
            await fixture.Auth.LdapLoginAsync(Login(), default);
            var user = SingleLdapUser(fixture);
            Check.That(user.Account == EmployeeNo && user.NormalizedAccount == EmployeeNo,
                "ZX 账号必须取工号，不能取目录登录名。");
            Check.That(user.EmployeeNo == EmployeeNo, "账号与工号必须一致。");
        }

        using (var conflict = CreateFixture(account: "zhangsan"))
        {
            // 本地账号占用了工号，但目录登录名完全不同；冲突必须按工号判定。
            conflict.Users.Items.Add(User.CreateLocal(
                EmployeeNo, "Local Owner", conflict.Passwords.Hash("Example123!")));
            var error = await CaptureAsync(() => conflict.Auth.LdapLoginAsync(Login(), default));
            Check.That(error is BusinessException { Code: ErrorCodes.Conflict },
                "本地账号占用该工号时必须返回冲突。");
            Check.That(conflict.Users.Items.Count(item => item.IsLdap) == 0, "冲突时不能创建 OA 账号。");
        }

        using (var fixture = CreateFixture(account: "zhangsan"))
        {
            var (role, sbu) = SeedGrants(fixture);
            var created = await fixture.UserService.CreateLdapAsync(fixture.User.Id, new CreateLdapUserDto
            {
                EmployeeNo = EmployeeNo,
                OpenImmediately = true,
                RoleIds = [role.Id],
                SbuIds = [sbu.Id]
            }, default);
            var user = fixture.Users.Items.Single(item => item.Id == created.Id);
            Check.That(user.Account == EmployeeNo, "预开通同样必须以工号作为 ZX 账号。");
        }
    }

    /// <summary>
    /// 客户端取消只结束等待，不结束底层同步调用：并发名额必须等调用真正结束后才归还，
    /// 否则反复取消请求就能让实际并发突破配置上限。
    /// </summary>
    private static async Task LimiterIsHeldUntilWorkFinishesAsync()
    {
        var runner = new LdapOperationRunner(maxConcurrentOperations: 1, totalTimeoutSeconds: 10);
        using var gate = new ManualResetEventSlim(false);
        var active = 0;
        var maxActive = 0;

        int Work(LdapOperation _)
        {
            var now = Interlocked.Increment(ref active);
            int observed;
            while ((observed = Volatile.Read(ref maxActive)) < now &&
                   Interlocked.CompareExchange(ref maxActive, now, observed) != observed)
            {
            }

            try
            {
                gate.Wait(TimeSpan.FromSeconds(10));
                return 1;
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }

        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var first = await CaptureAsync(() => runner.RunAsync(Work, cancelled.Token));
        Check.That(first is OperationCanceledException, "客户端取消应原样向上传递。");
        Check.That(Volatile.Read(ref active) == 1, "取消后底层调用仍在执行时，名额不能被提前归还。");

        using var second = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        var secondError = await CaptureAsync(() => runner.RunAsync(Work, second.Token));
        Check.That(secondError is OperationCanceledException, "名额未归还时后一个请求只能排队等待。");
        Check.That(Volatile.Read(ref active) == 1 && Volatile.Read(ref maxActive) == 1,
            "反复取消请求不能让实际并发超过配置上限。");

        gate.Set();
        var admitted = await runner.RunAsync(_ => 7, default);
        Check.That(admitted == 7 && Volatile.Read(ref maxActive) == 1,
            "底层调用结束后名额必须归还，否则后续请求会永久排队。");
    }

    /// <summary>
    /// 旧版本可能留下“达到阈值但没有过期时间”的计数键。只补递增阶段的 TTL 修不好它：
    /// 检查阶段会先返回 429，永远走不到递增。这里验证入口检查就会补回窗口。
    /// </summary>
    private static async Task GuardRepairsKeyWithoutExpiryAsync()
    {
        var options = new LdapOptions { MaxFailedAttempts = 3, FailureWindowSeconds = 120 };
        var redisOptions = new RedisSessionOptions { KeyPrefix = $"zx:identity:checks:{Guid.NewGuid():N}:" };
        var guard = new RedisLdapLoginGuard(TestRedis.Connection, options, redisOptions);
        var database = TestRedis.Connection.GetDatabase();

        try
        {
            for (var attempt = 0; attempt < options.MaxFailedAttempts; attempt++)
                await guard.RecordFailureAsync(EmployeeNo, default);

            var keys = FindKeys(redisOptions.KeyPrefix);
            Check.That(keys.Length == 1, "失败计数只能写一个键。");

            // 模拟旧版本留下的异常键：计数已达阈值，但没有过期时间。
            foreach (var key in keys)
                await database.KeyPersistAsync(key);
            Check.That(await database.KeyTimeToLiveAsync(keys[0]) is null, "构造的键应没有过期时间。");

            await Check.ErrorAsync(
                () => guard.EnsureAllowedAsync(EmployeeNo, default), ErrorCodes.TooManyRequests);

            var ttl = await database.KeyTimeToLiveAsync(keys[0]);
            Check.That(ttl is not null && ttl > TimeSpan.Zero &&
                ttl <= TimeSpan.FromSeconds(options.FailureWindowSeconds),
                "检查阶段必须给无 TTL 的旧键补回过期时间，否则该工号会永久 429。");
        }
        finally
        {
            foreach (var key in FindKeys(redisOptions.KeyPrefix))
                database.KeyDelete(key);
        }
    }

    /// <summary>
    /// 搜索阶段的结果码分类：sizeLimitExceeded 无论是错误码还是响应码都属于“结果歧义”，
    /// 49 只代表凭据被拒，不能在搜索阶段当成员工密码错误。
    /// </summary>
    private static void SearchFailuresAreClassified()
    {
        Check.That(LdapSearchFailure.IsAmbiguous(LdapSearchFailure.SizeLimitExceeded, null),
            "LdapException 的 4 必须识别为目录结果歧义。");
        Check.That(LdapSearchFailure.IsAmbiguous(null, LdapSearchFailure.SizeLimitExceeded),
            "DirectoryOperationException 的响应码 4 必须识别为目录结果歧义。");
        Check.That(!LdapSearchFailure.IsAmbiguous(LdapSearchFailure.InvalidCredentials, 0),
            "49 不属于结果歧义。");
        Check.That(!LdapSearchFailure.IsAmbiguous(null, null), "没有结果码时不属于歧义。");
    }

    private static async Task DependencyFailuresAreControlledAsync()
    {
        using var fixture = CreateFixture();

        fixture.Ldap.IsEnabled = false;
        var disabled = await CaptureAsync(() => fixture.Auth.LdapLoginAsync(Login(), default));
        Check.That(disabled is BusinessException { Code: ErrorCodes.ServiceUnavailable }, "LDAP 关闭应返回 503。");

        // LDAP 关闭不影响本地账号登录。
        var local = await fixture.LoginAsync();
        Check.That(!string.IsNullOrEmpty(local.AccessToken), "本地登录必须继续可用。");

        fixture.Ldap.IsEnabled = true;
        fixture.Ldap.Unavailable = true;
        var unavailable = await CaptureAsync(() => fixture.Auth.LdapLoginAsync(Login(), default));
        Check.That(unavailable is BusinessException { Code: ErrorCodes.ServiceUnavailable }, "LDAP 故障应返回 503。");
        Check.That(fixture.Users.Items.Count(item => item.IsLdap) == 0, "依赖故障不能创建账号，也不能计为密码错误。");
        Check.That(fixture.LdapGuard.Failures.Count == 0, "连接故障不能累计工号失败次数。");

        fixture.Ldap.Unavailable = false;
        var wrongPassword = await CaptureAsync(() =>
            fixture.Auth.LdapLoginAsync(new LdapLoginDto { EmployeeNo = EmployeeNo, Password = "wrong" }, default));
        Check.That(wrongPassword is BusinessException { Code: ErrorCodes.Unauthorized }, "密码错误应返回 401。");
        Check.That(fixture.LdapGuard.Failures[EmployeeNo] == 1, "明确的凭据错误才计失败次数。");

        // 空密码不发送到目录。
        var calls = fixture.Ldap.AuthenticateCalls;
        await Check.ErrorAsync(() => fixture.Auth.LdapLoginAsync(
            new LdapLoginDto { EmployeeNo = EmployeeNo, Password = string.Empty }, default), ErrorCodes.Unauthorized);
        Check.That(fixture.Ldap.AuthenticateCalls == calls, "空密码不应调用目录。");
    }

    private static async Task ProvisionWorksWithoutDirectoryAsync()
    {
        using var fixture = CreateFixture();
        fixture.Ldap.IsEnabled = false;
        fixture.Ldap.Unavailable = true;
        fixture.Ldap.Entries.Clear();
        var created = await fixture.UserService.CreateLdapAsync(fixture.User.Id,
            new CreateLdapUserDto { EmployeeNo = EmployeeNo }, default);
        var user = SingleLdapUser(fixture);
        Check.That(user.Id == created.Id && user.DisplayName == EmployeeNo &&
            user.LdapProvider is null && user.LdapSubject is null && fixture.Ldap.AuthenticateCalls == 0,
            "LDAP 关闭、故障或工号不存在时，预开通都不应访问目录或伪造身份。");
        await Check.ErrorAsync(() => fixture.UserService.CreateLdapAsync(fixture.User.Id,
            new CreateLdapUserDto { EmployeeNo = EmployeeNo }, default), ErrorCodes.Conflict);
    }

    private static async Task ProvisionedIdentityRequiresPasswordAndCannotBeReboundAsync()
    {
        using var fixture = CreateFixture();
        await fixture.UserService.CreateLdapAsync(fixture.User.Id,
            new CreateLdapUserDto { EmployeeNo = EmployeeNo }, default);
        var user = SingleLdapUser(fixture);
        var version = user.SecurityVersion;
        await Check.ErrorAsync(() => fixture.Auth.LdapLoginAsync(
            new LdapLoginDto { EmployeeNo = EmployeeNo, Password = "wrong" }, default), ErrorCodes.Unauthorized);
        Check.That(user.LdapProvider is null && user.LdapSubject is null && user.SecurityVersion == version,
            "知道预开通工号但密码错误，不能绑定身份。");

        await fixture.Auth.LdapLoginAsync(Login(), default);
        var subject = user.LdapSubject;
        fixture.Ldap.Entries[0] = fixture.Ldap.Entries[0] with { Subject = Guid.NewGuid().ToString("D") };
        await Check.ErrorAsync(() => fixture.Auth.LdapLoginAsync(Login(), default), ErrorCodes.Conflict);
        Check.That(user.LdapSubject == subject, "同工号的另一目录身份不能覆盖已有绑定。");
    }

    private static async Task UnavailableProvisionedAccountsStayUnboundAsync()
    {
        foreach (var state in new[] { "disabled", "rejected", "deleted", "locked", "directory-disabled", "directory-unknown" })
        {
            using var fixture = CreateFixture();
            await fixture.UserService.CreateLdapAsync(fixture.User.Id,
                new CreateLdapUserDto { EmployeeNo = EmployeeNo }, default);
            var user = SingleLdapUser(fixture);
            switch (state)
            {
                case "disabled": user.Disable(); break;
                case "rejected": user.RejectLdap(fixture.User.Id, fixture.Clock.GetUtcNow(), null); break;
                case "deleted": user.MarkDeleted(); break;
                case "locked": user.RecordLoginFailure(fixture.Clock.GetUtcNow(), 1, TimeSpan.FromMinutes(15)); break;
                case "directory-disabled":
                    fixture.Ldap.Entries[0] = fixture.Ldap.Entries[0] with { Status = LdapDirectoryStatus.Disabled };
                    break;
                case "directory-unknown":
                    fixture.Ldap.Entries[0] = fixture.Ldap.Entries[0] with { Status = LdapDirectoryStatus.Unknown };
                    break;
            }
            var version = user.SecurityVersion;
            await Check.ErrorAsync(() => fixture.Auth.LdapLoginAsync(Login(), default),
                state == "directory-unknown" ? ErrorCodes.ServiceUnavailable : ErrorCodes.Forbidden);
            Check.That(user.LdapProvider is null && user.LdapSubject is null && user.SecurityVersion == version,
                "不可用状态的预开通账号不得因 OA 认证而绑定或恢复。");
        }
    }

    private static Fixture CreateFixture(
        string status = LdapDirectoryStatus.Enabled, string? subject = null, string? account = null)
    {
        var fixture = new Fixture();
        // account 是目录登录名，默认与工号一致；两者不同的场景用于验证 ZX 账号取工号。
        fixture.Ldap.Entries.Add(new TestLdap.Entry(
            EmployeeNo, OaPassword, account ?? EmployeeNo, "张三", "IT 制造部", "luxshare-ad",
            subject ?? Guid.NewGuid().ToString("D"), status));
        return fixture;
    }

    private static RedisKey[] FindKeys(string prefix)
    {
        var database = TestRedis.Connection.GetDatabase();
        var keys = new List<RedisKey>();
        foreach (var endpoint in TestRedis.Connection.GetEndPoints())
            keys.AddRange(TestRedis.Connection.GetServer(endpoint).Keys(database.Database, prefix + "*"));
        return [.. keys];
    }

    private static (Role Role, Sbu Sbu) SeedGrants(Fixture fixture)
    {
        var role = Role.Create("OPERATOR", "现场操作员");
        var sbu = Sbu.Create("SBU1", "第一事业部");
        fixture.Roles.Add(role);
        fixture.Roles.AddPermission(RolePermission.Create(role.Id, IdentityPermissions.UserRead));
        fixture.Sbus.Items.Add(sbu);
        return (role, sbu);
    }

    private static LdapLoginDto Login() => new() { EmployeeNo = EmployeeNo, Password = OaPassword };

    private static User SingleLdapUser(Fixture fixture) =>
        fixture.Users.Items.Single(item => item.IsLdap);

    private static void KeepHistoricalDeletedAccountName(User user)
    {
        // 模拟升级前已软删除、但尚未释放标准化账号名的持久化记录。
        typeof(User).GetProperty(nameof(User.NormalizedAccount))!.SetValue(user, user.Account.ToUpperInvariant());
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
