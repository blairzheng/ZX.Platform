using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZX.Platform.Identity.Api.Controllers;
using ZX.Platform.Identity.Api.ErrorHandling;
using ZX.Platform.Identity.Api.Extensions;
using ZX.Platform.Identity.Api.Localization;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Authentication;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Application.Security;
using ZX.Platform.Identity.Application.Users;
using ZX.Platform.Identity.Infrastructure.Persistence;
using ZX.Platform.Identity.Infrastructure.Security;
using ZX.Platform.LowCode.Application.Metadata;
using ZX.Platform.LowCode.Application.Runtime;
using ZX.Platform.LowCode.Application.Schema;
using ZX.Platform.LowCode.Domain;
using ZX.Platform.LowCode.Infrastructure.Schema;

namespace ZX.Platform.Identity.AuthChecks;

/// <summary>
/// 低代码的完整 HTTP 回路。跑的是真实的 Controller / MetadataService / QueryEngine /
/// RecordEngine / SchemaChangeService，只有"数据放在哪"换成内存替身。
///
/// 这条链路之前一次都没跑过，而它恰好是前端接上以后第一眼就会踩到的地方：
/// 响应信封、算子字符串绑定、JSON → 值的落型、乐观锁错误码、结构变更闸门。
/// </summary>
internal static class LowCodeHttpChecks
{
    private const string App = "mes";
    private const string Entity = "station";

    public static async Task RunAsync()
    {
        using var fixture = new Fixture();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AuthenticationCookie:Secure"] = "false",
            ["AuthenticationJwt:SigningKey"] = fixture.Jwt.SigningKey,
            ["ConnectionStrings:IdentityRedis"] = TestRedis.ConnectionString,
            ["LowCode:Enabled"] = "true",
            // 只用来让 AddDbContext 通过校验，替身会顶掉真正连库的实现。
            ["ConnectionStrings:LowCodeDatabase"] = "User Id=UNUSED;Password=UNUSED;Data Source=127.0.0.1:1/UNUSED"
        });

        builder.Services.AddSingleton<TimeProvider>(fixture.Clock);
        builder.Services.AddIdentityAuthentication(builder.Configuration, builder.Environment);
        builder.Services.RemoveAll<IRoleRepository>();
        builder.Services.RemoveAll<IMenuRepository>();
        builder.Services.RemoveAll<AuthorizationInitializer>();
        builder.Services.AddScoped<IAuthSessionStore>(_ => fixture.Sessions);
        builder.Services.AddSingleton<IUserRepository>(fixture.Users);
        builder.Services.AddSingleton<IRoleRepository>(fixture.Roles);
        builder.Services.AddSingleton<IMenuRepository>(fixture.Menus);
        builder.Services.AddSingleton<IUserSbuRepository>(fixture.UserSbus);
        builder.Services.AddSingleton<ISbuRepository>(fixture.Sbus);
        builder.Services.AddSingleton<IPermissionCache>(fixture.PermissionCache);
        builder.Services.AddSingleton<IUnitOfWork>(fixture.Unit);
        builder.Services.AddSingleton<IPasswordHasher>(fixture.Passwords);
        builder.Services.AddScoped<IUserService>(_ => fixture.UserService);
        builder.Services.AddApiLocalization();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddIdentitySwagger();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);

        builder.Services.AddLowCodeModule(builder.Configuration);
        var inspector = new FakeSchemaInspector();
        var store = new FakeMetadataStore();
        builder.Services.RemoveAll<IMetadataStore>();
        builder.Services.RemoveAll<IRecordGateway>();
        builder.Services.RemoveAll<ISchemaInspector>();
        builder.Services.RemoveAll<ISchemaChangeExecutor>();
        builder.Services.AddSingleton<IMetadataStore>(store);
        builder.Services.AddSingleton<IRecordGateway>(new FakeRecordGateway());
        builder.Services.AddSingleton<ISchemaInspector>(inspector);
        builder.Services.AddSingleton<ISchemaChangeExecutor>(new FakeSchemaChangeExecutor(inspector));

        builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly)
            .AddJsonOptions(options => options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)
            .AddDataAnnotationsLocalization(options => options.DataAnnotationLocalizerProvider = (_, sbu) => sbu.Create(typeof(ValidationMessages)))
            .AddUnifiedApiBehavior()
            .ConfigureApplicationPartManager(manager =>
            {
                var gateway = typeof(ZX.Platform.Gateway.Controllers.WeatherForecastController).Assembly;
                foreach (var part in manager.ApplicationParts
                             .Where(item => item is AssemblyPart assembly && assembly.Assembly == gateway)
                             .ToList())
                    manager.ApplicationParts.Remove(part);
            });

        await using var app = builder.Build();
        app.UseRequestLocalization();
        app.UseExceptionHandler();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();

        using var client = new HttpClient(new HttpClientHandler { UseCookies = false })
        {
            BaseAddress = new Uri(app.Urls.Single())
        };

        async Task<Reply> SendAsync(HttpMethod method, string path, object? body = null, string? access = null)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add("X-Identity-CSRF", "1");
            request.Headers.AcceptLanguage.ParseAdd("zh-CN");
            if (access is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            if (body is not null) request.Content = JsonContent.Create(body);

            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            return new Reply(response.StatusCode, text.Length == 0 ? null : JsonDocument.Parse(text).RootElement.Clone());
        }

        var login = await SendAsync(HttpMethod.Post, "/api/Auth/login",
            new { account = "ADMIN", password = "Example123!" });
        Check.That(login.Status == HttpStatusCode.OK, "管理员必须能登录。");
        var access = login.Json!.Value.GetProperty("data").GetProperty("accessToken").GetString()!;

        // 未登录一律 401，且不会泄漏实体是否存在。
        Check.That((await SendAsync(HttpMethod.Get, $"/api/lowcode/entities/{App}")).Status == HttpStatusCode.Unauthorized,
            "低代码元数据接口必须要求登录。");

        var definition = JsonSerializer.SerializeToElement(TestDefinition(), MetadataJson.Options);

        var created = await SendAsync(HttpMethod.Post, $"/api/lowcode/entities/{App}",
            new { definition }, access);
        Check.That(created.Status == HttpStatusCode.Created, $"创建实体应返回 201，实际 {created.Status}：{created.Json}");

        var list = await SendAsync(HttpMethod.Get, $"/api/lowcode/entities/{App}", access: access);
        Check.That(list.Json!.Value.GetProperty("data").GetArrayLength() == 1, "实体列表应包含刚创建的实体。");

        // 草稿 revision 不对必须 409，否则两个人同时编辑会互相覆盖。
        var staleDraft = await SendAsync(HttpMethod.Put, $"/api/lowcode/entities/{App}/{Entity}/draft",
            new { revision = 99, definition }, access);
        Check.That(staleDraft.Status == HttpStatusCode.Conflict &&
            staleDraft.Json!.Value.GetProperty("code").GetInt32() == 600003,
            "过期 revision 保存草稿必须是 409 / 600003。");

        // 第一次发布：表还不存在，必须被结构变更挡住，而不是把元数据激活。
        var firstPublish = await SendAsync(HttpMethod.Post, $"/api/lowcode/entities/{App}/{Entity}/publish",
            access: access);
        var publishData = firstPublish.Json!.Value.GetProperty("data");
        Check.That(firstPublish.Status == HttpStatusCode.OK && !publishData.GetProperty("published").GetBoolean(),
            "第一次发布必须被结构变更挡住（published = false）。");

        var pending = publishData.GetProperty("pendingChanges");
        Check.That(pending.GetArrayLength() == 1 &&
            pending[0].GetProperty("kind").GetString() == "create_table",
            "第一次发布应生成一条 create_table 变更。");
        var changeId = pending[0].GetProperty("id").GetGuid();

        // 元数据没激活，业务接口必须报"不存在或尚未发布"——不能先能查后建表。
        var beforePublish = await SendAsync(HttpMethod.Post, $"/api/lowcode/records/{App}/{Entity}/query",
            new { take = 10 }, access);
        Check.That(beforePublish.Status == HttpStatusCode.NotFound &&
            beforePublish.Json!.Value.GetProperty("code").GetInt32() == 600002,
            "未发布实体的业务接口必须是 404 / 600002。");

        var script = await SendAsync(HttpMethod.Get, $"/api/lowcode/schema-changes/{changeId}/script", access: access);
        Check.That(script.Status == HttpStatusCode.OK &&
            script.Json!.Value.GetProperty("data").GetString()!.Contains("CREATE TABLE \"LC_MES_STATION\""),
            "导出的脚本必须包含建表语句。");

        // 导出之后仍然算未完成，发布必须继续被挡住。
        var blockedAgain = await SendAsync(HttpMethod.Post, $"/api/lowcode/entities/{App}/{Entity}/publish",
            access: access);
        Check.That(!blockedAgain.Json!.Value.GetProperty("data").GetProperty("published").GetBoolean(),
            "脚本导出之后发布仍然要被挡住。");

        var executed = await SendAsync(HttpMethod.Post, $"/api/lowcode/schema-changes/{changeId}/execute", access: access);
        Check.That(executed.Status == HttpStatusCode.OK &&
            executed.Json!.Value.GetProperty("data").GetProperty("status").GetString() == "applied",
            $"执行并核对后状态应为 applied，实际 {executed.Json}");

        var secondPublish = await SendAsync(HttpMethod.Post, $"/api/lowcode/entities/{App}/{Entity}/publish",
            access: access);
        var secondData = secondPublish.Json!.Value.GetProperty("data");
        Check.That(secondData.GetProperty("published").GetBoolean() && secondData.GetProperty("version").GetInt32() == 1,
            "结构变更执行完之后发布必须放行，版本为 1。");

        await CheckRecordsAsync(SendAsync, access);
        await app.StopAsync();
        Console.WriteLine("PASS low-code HTTP metadata lifecycle, schema gate and record CRUD");
    }

    private static async Task CheckRecordsAsync(
        Func<HttpMethod, string, object?, string?, Task<Reply>> send, string access)
    {
        var created = await send(HttpMethod.Post, $"/api/lowcode/records/{App}/{Entity}",
            new { values = new { code = "ST001", name = "一站" } }, access);
        Check.That(created.Status == HttpStatusCode.Created, $"新增记录应返回 201，实际 {created.Status}：{created.Json}");
        var id = created.Json!.Value.GetProperty("data").GetGuid();

        var query = await send(HttpMethod.Post, $"/api/lowcode/records/{App}/{Entity}/query",
            new { take = 10 }, access);
        var page = query.Json!.Value.GetProperty("data");
        Check.That(page.GetProperty("total").GetInt32() == 1, "查询应返回 1 条。");

        var row = page.GetProperty("rows")[0];
        Check.That(row.GetProperty("code").GetString() == "ST001", "行必须按字段 key 返回，不是列名。");
        Check.That(row.GetProperty("is_enabled").GetBoolean(), "boolean 必须转成 true/false，不是 1/0。");
        Check.That(row.GetProperty("id").GetGuid() == id, "id 必须是 Guid 字符串，不是 base64。");

        // 算子用字符串传，未知算子必须 400 / 610003，不能静默忽略。
        var badOperator = await send(HttpMethod.Post, $"/api/lowcode/records/{App}/{Entity}/query",
            new { filters = new[] { new { field = "code", op = "sql" } } }, access);
        Check.That(badOperator.Status == HttpStatusCode.BadRequest &&
            badOperator.Json!.Value.GetProperty("code").GetInt32() == 610003,
            $"未知算子必须是 400 / 610003，实际 {badOperator.Status}：{badOperator.Json}");

        // 未定义字段必须拒绝：静默丢弃会把客户端的拼写错误变成"保存成功但没生效"。
        var unknownField = await send(HttpMethod.Post, $"/api/lowcode/records/{App}/{Entity}",
            new { values = new { code = "ST002", nam = "拼错了" } }, access);
        Check.That(unknownField.Status == HttpStatusCode.BadRequest &&
            unknownField.Json!.Value.GetProperty("code").GetInt32() == 600001,
            "未定义字段必须 400 / 600001。");

        var staleUpdate = await send(HttpMethod.Put, $"/api/lowcode/records/{App}/{Entity}/{id}",
            new { revision = 99, values = new { name = "改不动" } }, access);
        Check.That(staleUpdate.Status == HttpStatusCode.Conflict &&
            staleUpdate.Json!.Value.GetProperty("code").GetInt32() == 600003,
            "乐观锁失败必须是 409 / 600003。");

        var updated = await send(HttpMethod.Put, $"/api/lowcode/records/{App}/{Entity}/{id}",
            new { revision = 1, values = new { name = "二站" } }, access);
        Check.That(updated.Status == HttpStatusCode.OK, $"修改应返回 200，实际 {updated.Status}：{updated.Json}");

        var found = await send(HttpMethod.Get, $"/api/lowcode/records/{App}/{Entity}/{id}", null, access);
        Check.That(found.Json!.Value.GetProperty("data").GetProperty("name").GetString() == "二站",
            "修改后单条查询必须是新值。");

        var deleted = await send(HttpMethod.Delete, $"/api/lowcode/records/{App}/{Entity}/{id}?revision=2", null, access);
        Check.That(deleted.Status == HttpStatusCode.OK, "删除应返回 200。");

        var afterDelete = await send(HttpMethod.Get, $"/api/lowcode/records/{App}/{Entity}/{id}", null, access);
        Check.That(afterDelete.Status == HttpStatusCode.NotFound, "删除后单条查询必须 404。");
    }

    /// <summary>站点表：一个唯一文本、一个布尔、一个日期，覆盖三类最容易错的绑定。</summary>
    private static EntityDefinition TestDefinition() => new()
    {
        AppCode = App,
        Code = Entity,
        Name = "站点",
        Fields =
        [
            new FieldDefinition
            {
                Key = "code", Type = FieldType.Text, Name = "站点编码",
                Required = true, MaxLength = 64, Unique = true, Sort = 10
            },
            new FieldDefinition
            {
                Key = "name", Type = FieldType.Text, Name = "站点名称", Required = true, Sort = 20
            },
            new FieldDefinition
            {
                Key = "is_enabled", Type = FieldType.Boolean, Name = "启用",
                Required = true, DefaultValue = "true", Sort = 30
            },
            new FieldDefinition
            {
                Key = "start_date", Type = FieldType.Date, Name = "启用日期", Sort = 40
            }
        ]
    };

    private sealed record Reply(HttpStatusCode Status, JsonElement? Json);
}
