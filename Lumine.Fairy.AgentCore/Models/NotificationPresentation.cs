namespace Lumine.Fairy.Models;

/// <summary>
/// 提供通知级别与来源在 UI 中的统一展示映射。
/// </summary>
public static class NotificationPresentation
{
    public static string GetLevelBackground(NotificationLevel level) => level switch
    {
        NotificationLevel.Success => "#0F2A24",
        NotificationLevel.Warning => "#2A2111",
        NotificationLevel.Error => "#2A1014",
        _ => "#111827"
    };

    public static string GetLevelBorderBrush(NotificationLevel level) => level switch
    {
        NotificationLevel.Success => "#10B981",
        NotificationLevel.Warning => "#F59E0B",
        NotificationLevel.Error => "#F87171",
        _ => "#334155"
    };

    public static string GetLevelForeground(NotificationLevel level) => level switch
    {
        NotificationLevel.Success => "#D1FAE5",
        NotificationLevel.Warning => "#FDE68A",
        NotificationLevel.Error => "#FECACA",
        _ => "#E2E8F0"
    };

    public static string GetSystemMessageForeground(NotificationLevel level) => level switch
    {
        NotificationLevel.Success => "#86EFAC",
        NotificationLevel.Warning => "#FBBF24",
        NotificationLevel.Error => "#FCA5A5",
        _ => "#94A3B8"
    };

    public static string GetSourceLabel(NotificationSource source) => source switch
    {
        NotificationSource.Settings => "设置",
        NotificationSource.Chat => "聊天",
        NotificationSource.Voice => "语音",
        NotificationSource.WakeWord => "唤醒",
        NotificationSource.Index => "索引",
        NotificationSource.Tool => "工具",
        _ => "系统"
    };

    public static string GetSourceBadgeBackground(NotificationSource source) => source switch
    {
        NotificationSource.Settings => "#1D4ED8",
        NotificationSource.Chat => "#7C3AED",
        NotificationSource.Voice => "#0F766E",
        NotificationSource.WakeWord => "#9A3412",
        NotificationSource.Index => "#065F46",
        NotificationSource.Tool => "#374151",
        _ => "#334155"
    };

    public static string GetSourceBadgeForeground(NotificationSource source) => "#E2E8F0";

    public static string GetSourceIconGlyph(NotificationSource source) => source switch
    {
        NotificationSource.Settings => "⚙",
        NotificationSource.Chat => "💬",
        NotificationSource.Voice => "🔊",
        NotificationSource.WakeWord => "👂",
        NotificationSource.Index => "📚",
        NotificationSource.Tool => "🛠",
        _ => "ℹ"
    };
}