using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using ZX.Platform.Identity.Api.ErrorHandling;
using ZX.Platform.Identity.Api.Extensions;
using ZX.Platform.Identity.Api.Localization;
using ZX.Platform.Identity.Application.Factories;
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
        options.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(ValidationMessages));
    })
    .AddUnifiedApiBehavior();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddIdentitySwagger();
builder.Services.AddScoped<AuditableEntitySaveChangesInterceptor>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
var identityConnectionString =
    builder.Configuration.GetConnectionString("IdentityDatabase")
    ?? throw new InvalidOperationException(
        "未配置 IdentityDatabase 数据库连接字符串。");

builder.Services.AddDbContext<PlatformIdentityDbContext>((serviceProvider, options) =>
{
    options.UseSqlServer(identityConnectionString);
    options.AddInterceptors(serviceProvider.GetRequiredService<AuditableEntitySaveChangesInterceptor>());
});
builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IFactoryRepository, FactoryRepository>();
builder.Services.AddScoped<IUnitOfWork>(serviceProvider =>
    serviceProvider.GetRequiredService<PlatformIdentityDbContext>());
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IFactoryService, FactoryService>();
builder.Services.AddScoped<IUserFactoryRepository, UserFactoryRepository>();

var app = builder.Build();

// 先还原可信代理提供的客户端信息，限流随后使用 RemoteIpAddress。
app.UseForwardedHeaders();

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
