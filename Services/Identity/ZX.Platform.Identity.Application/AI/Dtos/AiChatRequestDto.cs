using System.ComponentModel.DataAnnotations;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;

namespace ZX.Platform.Identity.Application.AI.Dtos;

public sealed class AiChatRequestDto
{
    [Required(ErrorMessage = "Validation.AiMessagesRequired")]
    [MinLength(1, ErrorMessage = "Validation.AiMessagesRequired")]
    [MaxLength(21, ErrorMessage = "Validation.AiMessagesMaxLength")]
    public List<AiChatMessageDto> Messages { get; set; } = [];

    public void ValidateConversation()
    {
        // 只接收完整的用户/助手轮次；系统提示、模型及工具均由服务端控制。
        if (Messages is null || Messages.Count is < 1 or > 21 || Messages.Count % 2 == 0 ||
            Messages.Where((message, index) => message is null ||
                message.Role != (index % 2 == 0 ? "user" : "assistant") ||
                string.IsNullOrWhiteSpace(message.Content) || message.Content.Length > 16000).Any())
            throw new BusinessException(ErrorCodes.ValidationFailed, "AI.InvalidConversation");

        if (Messages.Sum(message => (long)message.Content.Length) > 48000)
            throw new BusinessException(ErrorCodes.ValidationFailed, "AI.ConversationTooLong");
    }
}

public sealed class AiChatMessageDto
{
    [Required(ErrorMessage = "Validation.AiRoleInvalid")]
    [RegularExpression("^(user|assistant)$", ErrorMessage = "Validation.AiRoleInvalid")]
    public string Role { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.AiContentRequired")]
    [MaxLength(16000, ErrorMessage = "Validation.AiContentMaxLength")]
    public string Content { get; set; } = string.Empty;
}
