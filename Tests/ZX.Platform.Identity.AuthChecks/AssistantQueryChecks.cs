using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using ZX.Platform.Identity.Api.AI.Queries;
using ZX.Platform.Identity.Api.Authorization;
using ZX.Platform.Identity.Api.Contracts;
using ZX.Platform.Identity.Application.AI;
using ZX.Platform.Identity.Application.AI.Queries;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.AuthChecks;

// Offline MVC checks: the application is built and mapped, never started or bound to a port.
internal static class AssistantQueryChecks
{
    public static async Task RunAsync()
    {
        var user = User.CreateLocal("AI_QUERY_USER", "AI query user", "test-hash");
        var users = new TestUsers();
        users.Add(user);
        var sbu = Sbu.Create("AI_QUERY_SBU", "AI query SBU");
        var userSbus = new TestUserSbus(users);
        userSbus.Sbus.Add(sbu);
        userSbus.Items.Add(UserSbu.Create(user.Id, sbu.Id, false));
        var state = new AssistantQueryCheckState(user.Id);
        var endpoints = new AssistantQueryCheckEndpoints();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "AssistantQueryChecks",
            Args = []
        });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddControllers().ConfigureApplicationPartManager(parts =>
        {
            parts.ApplicationParts.Clear();
            parts.ApplicationParts.Add(new AssemblyPart(typeof(AssistantQueryCheckController).Assembly));
            parts.FeatureProviders.Clear();
            parts.FeatureProviders.Add(new AssistantQueryCheckControllerProvider());
        });
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSingleton<EndpointDataSource>(endpoints);
        builder.Services.AddSingleton(state);
        builder.Services.AddSingleton<IUserSbuRepository>(userSbus);
        builder.Services.AddScoped<CurrentSbuContext>();
        builder.Services.AddAuthentication(AssistantQueryCheckAuthentication.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, AssistantQueryCheckAuthentication>(
                AssistantQueryCheckAuthentication.SchemeName, _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy(AssistantQueryCheckState.ReadPolicy,
            policy => policy.RequireAuthenticatedUser().RequireClaim("query.read", "true")));
        builder.Services.AddAuthorization(options => options.AddPolicy(AssistantQueryCheckState.ExtraPolicy,
            policy => policy.RequireAuthenticatedUser().RequireClaim("query.extra", "true")));
        builder.Services.AddSingleton(new AssistantLimits { MaxToolResultBytes = 8192 });
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<AiQueryCatalog>();
        builder.Services.AddScoped<IAssistantQueries, AssistantQueries>();

        await using var app = builder.Build();
        app.MapControllers();
        endpoints.Sources = ((IEndpointRouteBuilder)app).DataSources.ToArray();

        var catalog = app.Services.GetRequiredService<AiQueryCatalog>();
        var entries = catalog.Entries;
        Check.That(entries.Count == 9, "The offline catalog must contain only its nine marked GET actions.");
        Check.That(entries.All(entry => entry.Action.ControllerTypeInfo.AsType() == typeof(AssistantQueryCheckController)),
            "The check host must not discover production controllers.");
        Check.That(entries.All(entry => entry.Id is not ("AssistantQueryCheck.Unmarked" or
            "AssistantQueryCheck.Write" or "AssistantQueryCheck.Anonymous")),
            "Unmarked GET, marked PUT and AllowAnonymous actions must not enter the AI catalog.");
        Check.That(entries.Single(entry => entry.Id == "AssistantQueryCheck.List").Description is null,
            "A method-level AiQuery marker must work without any description.");
        Check.That(entries.Single(entry => entry.Id == "AssistantQueryCheck.Scoped").RequiresSbu,
            "The catalog must preserve the original RequireSbu requirement.");

        var contexts = app.Services.GetRequiredService<IHttpContextAccessor>();
        async Task<JsonElement> CallAsync(Func<IAssistantQueries, Task<AssistantToolResult>> operation,
            bool canRead = true, string credential = "allow", string? selectedSbu = null, bool canReadExtra = true)
        {
            await using var scope = app.Services.CreateAsyncScope();
            var outer = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            outer.Request.Scheme = "http";
            outer.Request.Host = new HostString("localhost");
            outer.Request.Headers.Authorization = $"Check {credential}";
            if (selectedSbu is not null) outer.Request.Headers["X-SBU-ID"] = selectedSbu;
            outer.User = state.Principal(canRead, canReadExtra: canReadExtra);
            contexts.HttpContext = outer;
            try
            {
                var result = await operation(scope.ServiceProvider.GetRequiredService<IAssistantQueries>());
                Check.That(ReferenceEquals(contexts.HttpContext, outer),
                    "Executing an isolated query must preserve the chat request's HttpContext.");
                using var document = JsonDocument.Parse(result.Content);
                return document.RootElement.Clone();
            }
            finally
            {
                contexts.HttpContext = null;
            }
        }

        Task<JsonElement> ExecuteAsync(string action, string parameters = "{}", string? filters = null,
            bool canRead = true, string credential = "allow", string? selectedSbu = null)
            => CallAsync(queries => queries.ExecuteAsync($"AssistantQueryCheck.{action}", Json(parameters),
                filters is null ? null : Json(filters), CancellationToken.None), canRead, credential, selectedSbu);

        var directory = await CallAsync(queries => queries.ListAsync(null, 0, CancellationToken.None));
        Check.That(directory.GetProperty("queries").GetArrayLength() == 8 &&
            directory.GetProperty("hasMore").GetBoolean() && directory.GetProperty("nextOffset").GetInt32() == 8,
            "More than eight authorized queries must produce a usable directory continuation.");
        var remaining = await CallAsync(queries => queries.ListAsync(null, 8, CancellationToken.None));
        Check.That(remaining.GetProperty("queries").GetArrayLength() == 1 &&
            !remaining.GetProperty("hasMore").GetBoolean() && remaining.GetProperty("nextOffset").ValueKind == JsonValueKind.Null,
            "The next directory page must expose the ninth query and stop at the end.");
        var allIds = directory.GetProperty("queries").EnumerateArray()
            .Concat(remaining.GetProperty("queries").EnumerateArray())
            .Select(entry => entry.GetProperty("id").GetString()).ToArray();
        Check.That(allIds.Distinct(StringComparer.Ordinal).Count() == 9 &&
            allIds.SequenceEqual(entries.Select(entry => entry.Id)),
            "Paging must discover every authorized ID in order without omissions or duplicates.");
        var limited = await CallAsync(queries => queries.ListAsync(null, 0, CancellationToken.None), canReadExtra: false);
        Check.That(limited.GetProperty("queries").GetArrayLength() == 8 &&
            !limited.GetProperty("hasMore").GetBoolean() && limited.GetProperty("nextOffset").ValueKind == JsonValueKind.Null &&
            limited.GetProperty("queries").EnumerateArray().All(entry =>
                entry.GetProperty("id").GetString() != "AssistantQueryCheck.Restricted") &&
            limited.GetProperty("queries").EnumerateArray().Any(entry =>
                entry.GetProperty("id").GetString() == "AssistantQueryCheck.ZExtra"),
            "A denied query must not consume a visible page slot or create a false continuation.");
        var limitedEnd = await CallAsync(queries => queries.ListAsync(null, 8, CancellationToken.None), canReadExtra: false);
        Check.That(limitedEnd.GetProperty("queries").GetArrayLength() == 0 &&
            !limitedEnd.GetProperty("hasMore").GetBoolean(),
            "Directory offsets must count permitted results rather than denied catalog entries.");
        Status(await CallAsync(queries => queries.ListAsync(null, -1, CancellationToken.None)), "invalid_arguments");
        var hidden = await CallAsync(queries => queries.ListAsync(null, 0, CancellationToken.None), canRead: false);
        Check.That(hidden.GetProperty("queries").GetArrayLength() == 0,
            "The directory must hide queries denied by their original functional permission.");

        var before = state.TotalCalls;
        Status(await ExecuteAsync("List", canRead: false, credential: "deny"), "forbidden");
        Status(await ExecuteAsync("List", credential: "deny"), "forbidden");
        Status(await ExecuteAsync("List", credential: "other-user"), "forbidden");
        Check.That(state.TotalCalls == before,
            "Denied permission, a revoked credential and a different authenticated subject must not execute an action.");
        foreach (var action in new[] { "Unmarked", "Write", "Anonymous", "Missing" })
            Status(await ExecuteAsync(action), "unknown_query");
        Check.That(state.TotalCalls == before, "Unknown query IDs must never execute actions or mutations.");

        var full = await ExecuteAsync("List", filters: """{"name":{"eq":"ALPHA"}}""");
        Status(full, "ok");
        Check.That(full.GetProperty("isComplete").GetBoolean() &&
            full.GetProperty("sourceTotal").GetInt32() == 2 &&
            full.GetProperty("sourceRowCount").GetInt32() == 2 &&
            full.GetProperty("matchedRowCount").GetInt32() == 1,
            "Filtering must preserve source counts and match text without regard to case.");
        Check.That(full.GetProperty("columns").EnumerateArray().Select(value => value.GetString())
                .SequenceEqual(new[] { "name", "quantity", "note" }) &&
            full.GetProperty("rows")[0][1].GetInt32() == 12 &&
            full.GetProperty("rows")[0][2].ValueKind == JsonValueKind.Null &&
            full.GetProperty("omittedFields").EnumerateArray().Any(value => value.GetString() == "rowKey") &&
            !full.GetRawText().Contains("hidden-alpha", StringComparison.Ordinal),
            "Compact results must retain scalar values and nulls while explicitly removing only rowKey.");
        Status(await ExecuteAsync("List", filters: """{"missing":"value"}"""), "invalid_arguments");
        Status(await ExecuteAsync("List", filters: """{"rowKey":"hidden-alpha"}"""), "invalid_arguments");
        Status(await ExecuteAsync("List", filters: """{"quantity":{"contains":"1"}}"""), "invalid_arguments");

        var partial = await ExecuteAsync("Partial");
        Status(partial, "ok");
        Check.That(!partial.GetProperty("isComplete").GetBoolean() &&
            partial.GetProperty("completeness").GetString() == "partial" &&
            partial.GetProperty("sourceTotal").GetInt32() == 10,
            "A total larger than the returned page must never be represented as a complete dataset.");
        var unknown = await ExecuteAsync("Array");
        Check.That(!unknown.GetProperty("isComplete").GetBoolean() &&
            unknown.GetProperty("completeness").GetString() == "unknown",
            "A list without a declared total must not be assumed complete.");
        var completeArray = await ExecuteAsync("ZExtra");
        Status(completeArray, "ok");
        Check.That(completeArray.GetProperty("isComplete").GetBoolean() &&
            completeArray.GetProperty("completeness").GetString() == "complete" &&
            completeArray.GetProperty("completenessBasis").GetString() == "endpoint_contract" &&
            completeArray.GetProperty("sourceTotal").ValueKind == JsonValueKind.Null &&
            completeArray.GetProperty("sourceRowCount").GetInt32() == 2 &&
            completeArray.GetProperty("rows").GetArrayLength() == 2,
            "An explicitly complete list contract may establish completeness without inventing a source total.");
        var completeTree = await ExecuteAsync("Restricted");
        Status(completeTree, "ok");
        Check.That(completeTree.GetProperty("isComplete").GetBoolean() &&
            completeTree.GetProperty("completeness").GetString() == "complete" &&
            completeTree.GetProperty("completenessBasis").GetString() == "endpoint_contract" &&
            completeTree.GetProperty("collectionKind").GetString() == "tree" &&
            completeTree.GetProperty("rootCount").GetInt32() == 1 &&
            completeTree.GetProperty("nodeCount").GetInt32() == 2 &&
            completeTree.GetProperty("data")[0].GetProperty("children")[0].GetProperty("name").GetString() == "Child" &&
            !completeTree.TryGetProperty("columns", out _) && !completeTree.TryGetProperty("rows", out _),
            "An explicitly complete tree must retain nested nodes and distinguish roots from total nodes.");
        Status(await ExecuteAsync("Restricted", filters: """{"name":"Root"}"""), "invalid_arguments");
        var oversized = await ExecuteAsync("Oversized");
        Status(oversized, "too_large");
        Check.That(!oversized.TryGetProperty("rows", out _) && !oversized.TryGetProperty("data", out _) &&
            !oversized.TryGetProperty("columns", out _),
            "An over-budget result must not expose partial rows, columns or data.");

        var bound = await ExecuteAsync("Validate", """{"count":2,"term":"A & B/中文"}""");
        Status(bound, "ok");
        Check.That(bound.GetProperty("data").GetProperty("count").GetInt32() == 2 &&
            bound.GetProperty("data").GetProperty("term").GetString() == "A & B/中文",
            "Original MVC DTO binding must preserve query aliases, types and encoded characters.");
        before = state.ValidationCalls;
        var invalid = await ExecuteAsync("Validate", """{"count":0,"term":"valid"}""");
        Check.That(invalid.GetProperty("status").GetString() != "ok" && state.ValidationCalls == before,
            "MVC data-annotation validation must reject an invalid DTO before the action executes.");
        var detailId = Guid.NewGuid();
        var detail = await ExecuteAsync("Detail", JsonSerializer.Serialize(new { id = detailId, include = true }));
        Status(detail, "ok");
        Check.That(detail.GetProperty("data").GetProperty("id").GetGuid() == detailId &&
            detail.GetProperty("data").GetProperty("include").GetBoolean(),
            "Original route and query binding must execute through the MVC endpoint.");
        before = state.TotalCalls;
        Status(await ExecuteAsync("Detail", """{"id":"not-a-guid"}"""), "invalid_arguments");
        Status(await ExecuteAsync("List", """{"sbuId":"forged"}"""), "invalid_arguments");
        Check.That(state.TotalCalls == before, "Undeclared parameters and invalid typed values must not execute actions.");

        before = state.ScopedCalls;
        Status(await ExecuteAsync("Scoped"), "selection_required");
        Status(await ExecuteAsync("Scoped", selectedSbu: Guid.NewGuid().ToString()), "forbidden");
        Check.That(state.ScopedCalls == before, "Missing or unassigned SBU selections must not execute the scoped action.");
        var scoped = await ExecuteAsync("Scoped", selectedSbu: sbu.Id.ToString());
        Status(scoped, "ok");
        Check.That(state.ScopedCalls == before + 1 &&
            scoped.GetProperty("data").GetProperty("sbuId").GetGuid() == sbu.Id &&
            scoped.GetProperty("sbuId").GetGuid() == sbu.Id,
            "A permitted SBU must pass the actual RequireSbu filter and establish CurrentSbuContext.");

        Console.WriteLine("Assistant query checks passed (in-memory MVC; no listening server or external services).");
    }

    private static JsonElement Json(string source)
    {
        using var document = JsonDocument.Parse(source);
        return document.RootElement.Clone();
    }

    private static void Status(JsonElement result, string expected)
        => Check.That(result.GetProperty("status").GetString() == expected,
            $"Expected assistant query status '{expected}', got {result.GetRawText()}.");
}

public sealed class AssistantQueryCheckState(Guid userId)
{
    public const string ReadPolicy = "assistant.query.check.read";
    public const string ExtraPolicy = "assistant.query.check.extra";
    public Guid UserId { get; } = userId;
    public int TotalCalls { get; set; }
    public int ValidationCalls { get; set; }
    public int ScopedCalls { get; set; }

    public ClaimsPrincipal Principal(bool canRead, Guid? subject = null, bool canReadExtra = true)
        => new(new ClaimsIdentity(new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, (subject ?? UserId).ToString()),
            new Claim("query.read", canRead ? "true" : "false"),
            new Claim("query.extra", canReadExtra ? "true" : "false")
        }, AssistantQueryCheckAuthentication.SchemeName));
}

// Opted in only by this check's feature provider; other check hosts keep their normal controller set.
[NonController]
[ApiController]
[Route("checks/assistant/[action]")]
[Authorize(Policy = AssistantQueryCheckState.ReadPolicy)]
public sealed class AssistantQueryCheckController(AssistantQueryCheckState state, CurrentSbuContext currentSbu)
    : ControllerBase
{
    [AiQuery]
    [HttpGet]
    public ApiResponse<object> List() => Reply(new
    {
        list = new[]
        {
            new { rowKey = "hidden-alpha", name = "Alpha", quantity = 12, note = (string?)null },
            new { rowKey = "hidden-beta", name = "Beta", quantity = 5, note = "kept" }
        },
        total = 2
    });

    [AiQuery]
    [HttpGet]
    public ApiResponse<object> Partial() => Reply(new { list = new[] { new { name = "Page one" } }, total = 10 });

    [AiQuery]
    [HttpGet]
    public ApiResponse<object> Array() => Reply(new[] { new { name = "No total" } });

    [AiQuery]
    [HttpGet]
    public ApiResponse<object> Oversized() => Reply(new
    {
        list = new[] { new { name = new string('x', 32000) } }, total = 1
    });

    [AiQuery]
    [HttpGet]
    public ApiResponse<object> Validate([FromQuery] AssistantQueryCheckInput input)
    {
        state.ValidationCalls++;
        return Reply(new { count = input.Count, term = input.Term });
    }

    [AiQuery]
    [HttpGet("{id}")]
    public ApiResponse<object> Detail(Guid id, [FromQuery] bool include = false) => Reply(new { id, include });

    [AiQuery]
    [HttpGet]
    [RequireSbu]
    public ApiResponse<object> Scoped()
    {
        state.ScopedCalls++;
        return Reply(new { sbuId = currentSbu.SbuId });
    }

    [AiQuery(ResultShape = AiQueryResultShape.CompleteTree)]
    [HttpGet]
    [Authorize(Policy = AssistantQueryCheckState.ExtraPolicy)]
    public ApiResponse<object> Restricted() => Reply(new[]
    {
        new { name = "Root", children = new[] { new { name = "Child", children = System.Array.Empty<object>() } } }
    });

    [AiQuery(ResultShape = AiQueryResultShape.CompleteList)]
    [HttpGet]
    public ApiResponse<object> ZExtra() => Reply(new[] { new { name = "First" }, new { name = "Second" } });

    [HttpGet]
    public ApiResponse<object> Unmarked() => Reply(new { unmarked = true });

    [AiQuery]
    [HttpPut]
    public ApiResponse<object> Write() => Reply(new { mutated = true });

    [AiQuery]
    [HttpGet]
    [AllowAnonymous]
    public ApiResponse<object> Anonymous() => Reply(new { anonymous = true });

    private ApiResponse<object> Reply(object data)
    {
        state.TotalCalls++;
        return new ApiResponse<object> { Code = 0, Message = "ok", Data = data };
    }
}

public sealed class AssistantQueryCheckInput
{
    [Range(1, 5)]
    public int Count { get; set; } = 1;

    [Required]
    [FromQuery(Name = "term")]
    public string? Term { get; set; }
}

internal sealed class AssistantQueryCheckControllerProvider : ControllerFeatureProvider
{
    protected override bool IsController(TypeInfo typeInfo)
        => typeInfo.AsType() == typeof(AssistantQueryCheckController);
}

internal sealed class AssistantQueryCheckEndpoints : EndpointDataSource
{
    public IReadOnlyList<EndpointDataSource> Sources { get; set; } = [];
    public override IReadOnlyList<Endpoint> Endpoints => Sources.SelectMany(source => source.Endpoints).ToArray();
    public override IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);
}

internal sealed class AssistantQueryCheckAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, AssistantQueryCheckState state)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AssistantQueryCheck";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var credential = Request.Headers.Authorization.ToString();
        if (credential is not ("Check allow" or "Check deny" or "Check other-user"))
            return Task.FromResult(AuthenticateResult.NoResult());
        var principal = state.Principal(credential != "Check deny",
            credential == "Check other-user" ? Guid.NewGuid() : null);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
