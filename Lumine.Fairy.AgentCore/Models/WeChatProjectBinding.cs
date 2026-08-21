namespace Lumine.Fairy.Models;

public sealed class WeChatProjectBinding
{
    public string ProjectName { get; set; } = string.Empty;

    public string ConversationName { get; set; } = string.Empty;

    public List<string> ConversationAliases { get; set; } = [];

    public bool IsGroupConversation { get; set; }

    public List<string> MemberNames { get; set; } = [];
}