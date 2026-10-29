using System.Text.Json.Serialization;
using ZX.Platform.Identity.Api.ErrorHandling;
using ZX.Platform.Identity.Api.Extensions;
using ZX.Platform.Identity.Api.Localization;
using ZX.Platform.Identity.Application.Sbus;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Application.Security;
using ZX.Platform.Identity.Application.Users;
using ZX.Platform.Identity.Infrastructure.Persistence;
using ZX.Platform.Identity.Infrastructure.Persistence.Interceptors;
using ZX.Platform.Identity.Infrastructure.Persistence.Repositories;
using ZX.Platform.Identity.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddTrustedForwardedHeaders(builder.Configuration);
builder.Services.AddRequestLogging();
builder.Services.AddApiLocalization();
builder.Services.AddApiRateLimiting(builder.Configuration);
builder.Services.AddIdentityAuthentication(builder.Configuration, builder.Environment);
// WriteAsJsonAsync 使用 HTTP JSON 配置，与 MVC 分别设置相同的空值规则。
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddControllers()
    .AddJsonOptions(option =>
    {
        option.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    })
    .AddDataAnnotationsLocalization(options =>
    {
        options.DataAnnotationLocalizerProvider = (_, sbu) => sbu.Create(typeof(ValidationMessages));
    })
    .AddUnifiedApiBehavior();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddIdentitySwagger();
builder.Services.AddScoped<AuditableEntitySaveChangesInterceptor>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddIdentityDatabase(builder.Configuration);
builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ISbuRepository, SbuRepository>();
builder.Services.AddScoped<IUnitOfWork>(serviceProvider =>
    serviceProvider.GetRequiredService<PlatformIdentityDbContext>());
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ISbuService, SbuService>();
builder.Services.AddScoped<IUserSbuRepository, UserSbuRepository>();

var app = builder.Build();

// 数据库结构和首个管理员由外部流程准备；启动时只同步权限目录和默认菜单，不执行数据库 DDL。
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<AuthorizationInitializer>().InitializeAsync(CancellationToken.None);
}

// 先还原可信代理提供的客户端信息，限流随后使用 RemoteIpAddress。
app.UseForwardedHeaders();
// 包住异常处理器，摘要才能记录业务异常转换后的最终 HTTP 状态码。
app.UseHttpLogging();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint(
            "/swagger/v1/swagger.json",
            "ZX.Platform.Identity.Api v1");
    });
}

// 在异常处理和 MVC 之前设置语言，使成功、业务异常及校验提示使用同一语言。
app.UseRequestLocalization();
app.UseExceptionHandler();

app.UseHttpsRedirection();

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
