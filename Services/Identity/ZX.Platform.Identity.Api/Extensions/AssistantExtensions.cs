using System.ComponentModel.DataAnnotations;
using ZX.Platform.Identity.Api.AI.Queries;
using ZX.Platform.Identity.Application.AI;
using ZX.Platform.Identity.Application.AI.Knowledge;
using ZX.Platform.Identity.Application.AI.Queries;
using ZX.Platform.Identity.Infrastructure.AI;
using ZX.Platform.Identity.Infrastructure.AI.Knowledge;

namespace ZX.Platform.Identity.Api.Extensions;

public static class AssistantExtensions
{
    public static IServiceCollection AddPlatformAssistant(this IServiceCollection services, IConfiguration configuration,
        IHostEnvironment environment)
    {
        var options = configuration.GetSection("AI").Get<AssistantOptions>() ?? new AssistantOptions();
        Validator.ValidateObject(options, new ValidationContext(options), validateAllProperties: true);
        services.AddSingleton(options);
        services.AddSingleton(new AssistantLimits
        {
            MaxModelRounds = options.MaxModelRounds,
            MaxToolResultBytes = options.MaxToolResultBytes
        });
        services.AddSingleton<IProjectKnowledge>(_ => new ProjectKnowledgeStore(
            Path.Combine(environment.ContentRootPath, "AI", "Knowledge", "project-sources.json")));
        services.AddScoped<IAssistantService, AssistantService>();
        services.AddHttpContextAccessor();
        services.AddSingleton<AiQueryCatalog>();
        services.AddScoped<IAssistantQueries, AssistantQueries>();
        services.AddScoped<IAssistantTools, AssistantTools>();
        services.AddHttpClient<IAssistantModel, DeepSeekAssistantModel>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            client.MaxResponseContentBufferSize = 1024 * 1024;
        })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .RedactLoggedHeaders(_ => true);
        return services;
    }
}
