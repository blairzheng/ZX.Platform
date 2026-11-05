using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ZX.Platform.Identity.Application.AI;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;

namespace ZX.Platform.Identity.Infrastructure.AI;

public sealed class DeepSeekAssistantModel(HttpClient http, AssistantOptions options,
    ILogger<DeepSeekAssistantModel> logger) : IAssistantModel
{
    public async Task<AssistantCompletion> CompleteAsync(IReadOnlyList<AssistantMessage> messages, bool allowTools,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate((options.BaseUrl ?? string.Empty).Trim().TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(baseUri.UserInfo) ||
            !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment) ||
            string.IsNullOrWhiteSpace(options.Model) || string.IsNullOrWhiteSpace(options.ApiKey) ||
            options.ApiKey.Any(char.IsControl))
            throw Failure("AI.NotConfigured");

        var wireMessages = messages.Select(message =>
        {
            var item = new Dictionary<string, object?> { ["role"] = message.Role, ["content"] = message.Content };
            if (message.ToolCallId is not null) item["tool_call_id"] = message.ToolCallId;
            if (message.ReasoningContent is not null) item["reasoning_content"] = message.ReasoningContent;
            if (message.ToolCalls is not null)
                item["tool_calls"] = message.ToolCalls.Select(call => new
                {
                    id = call.Id,
                    type = "function",
                    function = new { name = call.Name, arguments = call.Arguments }
                }).ToArray();
            return item;
        }).ToArray();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "chat/completions"))
        {
            Content = JsonContent.Create(new
            {
                model = options.Model.Trim(),
                messages = wireMessages,
                tools = AssistantToolDefinitions.Value,
                tool_choice = allowTools ? "auto" : "none",
                max_tokens = options.MaxTokens,
                stream = false
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey.Trim());
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // 不记录源码片段、业务数据、凭据、对话或上游错误正文。
                logger.LogWarning("Assistant model returned HTTP {StatusCode}.", (int)response.StatusCode);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    throw new BusinessException(ErrorCodes.TooManyRequests, "AI.Busy");
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw Failure("AI.AuthenticationFailed");
                throw Failure(response.StatusCode == HttpStatusCode.BadRequest ? "AI.ToolProtocolUnavailable" : "AI.Unavailable");
            }

            var completion = await response.Content.ReadFromJsonAsync<ModelResponse>(cancellationToken: cancellationToken);
            var choice = completion?.Choices?.FirstOrDefault();
            if (choice?.Message is null || choice.FinishReason is not ("stop" or "length" or "tool_calls"))
                throw Failure("AI.InvalidResponse");
            var message = choice.Message;
            if (message.Content?.Length > 16000 || message.ReasoningContent?.Length > 64000 || message.ToolCalls?.Count > 4)
                throw Failure("AI.InvalidResponse");
            var calls = new List<AssistantToolCall>();
            foreach (var call in message.ToolCalls ?? [])
            {
                if (call is null || string.IsNullOrWhiteSpace(call.Id) || call.Id.Length > 128 || call.Id.Any(char.IsControl) ||
                    call.Type != "function" || string.IsNullOrWhiteSpace(call.Function?.Name) ||
                    call.Function.Name.Length > 100 || call.Function.Arguments is null || call.Function.Arguments.Length > 8000 ||
                    calls.Any(existing => existing.Id == call.Id))
                    throw Failure("AI.InvalidResponse");
                calls.Add(new AssistantToolCall(call.Id, call.Function.Name, call.Function.Arguments));
            }
            if (calls.Count == 0 && (choice.FinishReason == "tool_calls" || string.IsNullOrWhiteSpace(message.Content)))
                throw Failure("AI.InvalidResponse");
            return new AssistantCompletion(options.Model.Trim(), message.Content, calls,
                choice.FinishReason == "length", message.ReasoningContent);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning("Assistant model connection failed. Error={Error}", exception.HttpRequestError);
            throw Failure("AI.Unavailable");
        }
        catch (JsonException)
        {
            throw Failure("AI.InvalidResponse");
        }
    }

    private static BusinessException Failure(string key) => new(ErrorCodes.ServiceUnavailable, key);

    private sealed class ModelResponse
    {
        public ModelResponse() { }
        public List<ModelChoice>? Choices { get; set; }
    }

    private sealed class ModelChoice
    {
        public ModelChoice() { }
        public ModelMessage? Message { get; set; }
        [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
    }

    private sealed class ModelMessage
    {
        public ModelMessage() { }
        public string? Content { get; set; }
        [JsonPropertyName("reasoning_content")] public string? ReasoningContent { get; set; }
        [JsonPropertyName("tool_calls")] public List<ModelToolCall>? ToolCalls { get; set; }
    }

    private sealed class ModelToolCall
    {
        public ModelToolCall() { }
        public string? Id { get; set; }
        public string? Type { get; set; }
        public ModelFunction? Function { get; set; }
    }

    private sealed class ModelFunction
    {
        public ModelFunction() { }
        public string? Name { get; set; }
        public string? Arguments { get; set; }
    }
}
