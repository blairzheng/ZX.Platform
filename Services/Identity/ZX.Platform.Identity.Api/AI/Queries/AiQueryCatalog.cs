using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using ZX.Platform.Identity.Api.Authorization;

namespace ZX.Platform.Identity.Api.AI.Queries;

// Discovery only: this catalog never calls an action or decides whether a user may read its data.
public sealed class AiQueryCatalog(EndpointDataSource endpoints, IApiDescriptionGroupCollectionProvider descriptions)
{
    private readonly Lazy<IReadOnlyList<AiQueryEndpoint>> _entries = new(() => Build(endpoints, descriptions));

    // Read after MapControllers; endpoint discovery is intentionally deferred until first use.
    public IReadOnlyList<AiQueryEndpoint> Entries => _entries.Value;

    private static IReadOnlyList<AiQueryEndpoint> Build(EndpointDataSource endpoints,
        IApiDescriptionGroupCollectionProvider descriptions)
    {
        var apiDescriptions = descriptions.ApiDescriptionGroups.Items.SelectMany(group => group.Items).ToArray();
        var candidates = new List<AiQueryEndpoint>();
        foreach (var endpoint in endpoints.Endpoints.OfType<RouteEndpoint>())
        {
            var action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
            var marker = action?.MethodInfo.GetCustomAttribute<AiQueryAttribute>(inherit: true);
            if (action is null || marker is null || !Enum.IsDefined(marker.ResultShape) || endpoint.RequestDelegate is null ||
                endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null ||
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0)
                continue;

            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
            if (methods is not { Count: 1 } || !HttpMethods.IsGet(methods[0])) continue;

            // Only stable, unambiguous controller/action IDs enter the model-visible query directory.
            var id = $"{action.ControllerName}.{action.ActionName}";
            if (!Regex.IsMatch(id, @"\A[A-Za-z][A-Za-z0-9_]*\.[A-Za-z][A-Za-z0-9_]*\z") || id.Length > 160)
                continue;

            var matches = apiDescriptions.Where(description =>
                description.ActionDescriptor.Id == action.Id &&
                string.Equals(description.HttpMethod, HttpMethods.Get, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) continue;
            var api = matches[0];
            var template = api.RelativePath?.Split('?', 2)[0];
            if (string.IsNullOrWhiteSpace(template) || template.StartsWith('/') ||
                template.Contains('\\') || template.Contains(':') || template.Any(char.IsControl))
                continue;

            // Framework cancellation is not model input. All other exposed inputs must be query or route scalars.
            if (action.Parameters.Any(parameter => parameter.ParameterType != typeof(CancellationToken) &&
                    parameter.BindingInfo?.BindingSource is { } source &&
                    source.Id != BindingSource.Query.Id && source.Id != BindingSource.Path.Id &&
                    source.Id != BindingSource.ModelBinding.Id))
                continue;

            var parameters = new List<AiQueryParameter>();
            var valid = true;
            foreach (var parameter in api.ParameterDescriptions)
            {
                if (parameter.Type == typeof(CancellationToken)) continue;
                var route = parameter.Source?.Id == BindingSource.Path.Id;
                if ((!route && parameter.Source?.Id != BindingSource.Query.Id) ||
                    !IsSupportedType(parameter.Type) || string.IsNullOrWhiteSpace(parameter.Name) ||
                    parameter.Name.Length > 160 || parameter.Name.Any(char.IsControl) ||
                    parameters.Any(existing => existing.Name.Equals(parameter.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    valid = false;
                    break;
                }
                parameters.Add(new AiQueryParameter(parameter.Name, parameter.Type,
                    route ? parameter.RouteInfo?.IsOptional != true : parameter.IsRequired,
                    route, parameter.DefaultValue, ShortDescription(parameter.ModelMetadata?.Description)));
            }
            if (!valid || parameters.Count > 30) continue;

            var summary = endpoint.Metadata.GetMetadata<IEndpointSummaryMetadata>()?.Summary;
            var description = ShortDescription(marker.Description) ?? ShortDescription(summary);
            var response = api.SupportedResponseTypes.FirstOrDefault(type => type.StatusCode is >= 200 and < 300)?.Type;
            var requiresSbu = endpoint.Metadata.GetMetadata<RequireSbuAttribute>() is not null ||
                action.MethodInfo.IsDefined(typeof(RequireSbuAttribute), inherit: true) ||
                action.ControllerTypeInfo.IsDefined(typeof(RequireSbuAttribute), inherit: true);
            candidates.Add(new AiQueryEndpoint(id, endpoint, action, description, parameters.ToArray(), requiresSbu,
                $"backend/Services/Identity/ZX.Platform.Identity.Api/Controllers/{action.ControllerName}Controller.cs",
                response?.FullName, template, marker.ResultShape));
        }

        // Multiple routes or overloads with the same ID are excluded, never picked arbitrarily.
        return candidates.GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1).Select(group => group.Single())
            .OrderBy(entry => entry.Id, StringComparer.Ordinal).ToArray();
    }

    public static bool IsSupportedType(Type? type)
    {
        if (type is null) return false;
        if (type.IsArray)
            return type.GetArrayRank() == 1 && IsScalar(type.GetElementType());
        return IsScalar(type);
    }

    private static bool IsScalar(Type? type)
    {
        if (type is null) return false;
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsEnum || type == typeof(string) || type == typeof(bool) || type == typeof(byte) ||
            type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) || type == typeof(int) ||
            type == typeof(uint) || type == typeof(long) || type == typeof(ulong) || type == typeof(float) ||
            type == typeof(double) || type == typeof(decimal) || type == typeof(Guid) ||
            type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) ||
            type == typeof(TimeOnly) || type == typeof(TimeSpan);
    }

    private static string? ShortDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length <= 1000 && !value.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t'))
            ? value : null;
    }
}

public sealed record AiQueryEndpoint(string Id, RouteEndpoint Endpoint, ControllerActionDescriptor Action,
    string? Description, IReadOnlyList<AiQueryParameter> Parameters, bool RequiresSbu, string SourcePath,
    string? ResponseType, string RouteTemplate, AiQueryResultShape ResultShape);

public sealed record AiQueryParameter(string Name, Type Type, bool IsRequired, bool IsRoute,
    object? DefaultValue, string? Description);
