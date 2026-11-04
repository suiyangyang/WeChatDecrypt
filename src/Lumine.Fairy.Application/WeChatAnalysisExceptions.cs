namespace Lumine.Fairy.Application;

/// <summary>
/// 白名单范围内没有待分析的新消息，本轮无需提交 QwenPaw。
/// </summary>
public sealed class NoPendingWeChatMessagesException : InvalidOperationException
{
    public NoPendingWeChatMessagesException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// 已经有一轮微信分析正在执行；请求可能已合并到当前轮次结束后立即补跑。
/// </summary>
public sealed class WeChatAnalysisRoundInProgressException : InvalidOperationException
{
    public WeChatAnalysisRoundInProgressException(string message)
        : base(message)
    {
    }
}