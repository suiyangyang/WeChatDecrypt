using Lumine.Fairy.Protocol;

namespace Lumine.Fairy.Application;

public static class WeChatAnalysisSelection
{
    public static bool IsSessionAllowed(
        ChatSession session,
        WeChatAnalysisSettings settings,
        IReadOnlyList<WeChatWhitelistEntry> whitelist)
    {
        if (!settings.EnableWhitelist) return true;

        var selectedIds = settings.WhitelistContactIds
            .Concat(settings.WhitelistCompanyIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedIds.Count == 0) return false;

        var sessionText = $"{session.Username}\n{session.DisplayName}";
        return whitelist
            .Where(entry => selectedIds.Contains(entry.Id))
            .Any(entry => MatchesSession(entry, session, sessionText));
    }

    private static bool MatchesSession(WeChatWhitelistEntry entry, ChatSession session, string sessionText)
    {
        if ((entry.ExactWeChatIds ?? []).Any(id => string.Equals(id?.Trim(), session.Username.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return entry.Aliases
            .Append(entry.Name)
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Any(alias => IsSafeFallbackAlias(alias) && sessionText.Contains(alias.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSafeFallbackAlias(string alias)
    {
        var value = alias.Trim();
        if (value.Length == 0) return false;

        // Very short ASCII aliases such as "AI" or "UC" are too common for substring matching.
        return !(value.Length <= 2 && value.All(static character => character <= 0x7F && (char.IsLetterOrDigit(character) || character == '_' || character == '-')));
    }

    public static IReadOnlyList<ChatMessage> SelectMessages(
        IReadOnlyList<ChatMessage> messages,
        WeChatAutomaticAnalysisMode mode,
        string? previousCursor)
    {
        if (messages.Count == 0) return [];

        if (mode == WeChatAutomaticAnalysisMode.Unanalyzed)
        {
            if (string.IsNullOrWhiteSpace(previousCursor)) return [];
            var separator = previousCursor.LastIndexOf(':');
            var previousId = separator > 0 ? previousCursor[..separator] : previousCursor;
            var previousIndex = messages.ToList().FindIndex(x => x.Id == previousId);
            return previousIndex >= 0 ? messages.Skip(previousIndex + 1).ToList() : messages;
        }

        var threshold = mode switch
        {
            WeChatAutomaticAnalysisMode.CurrentWeek => StartOfCurrentWeekUtc(),
            WeChatAutomaticAnalysisMode.OneMonth => DateTimeOffset.UtcNow.AddMonths(-1),
            _ => DateTimeOffset.MinValue,
        };

        return messages
            .Where(message => ToDateTimeOffset(message.Timestamp) >= threshold)
            .ToList();
    }

    public static string GetCursor(ChatMessage message)
        => $"{message.Id}:{message.Timestamp}";

    private static DateTimeOffset StartOfCurrentWeekUtc()
    {
        var localNow = DateTimeOffset.Now;
        var daysSinceMonday = ((int)localNow.DayOfWeek + 6) % 7;
        var monday = localNow.Date.AddDays(-daysSinceMonday);
        return new DateTimeOffset(monday, localNow.Offset).ToUniversalTime();
    }

    private static DateTimeOffset ToDateTimeOffset(long timestamp)
    {
        if (timestamp <= 0) return DateTimeOffset.MinValue;
        return timestamp > 10_000_000_000
            ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
            : DateTimeOffset.FromUnixTimeSeconds(timestamp);
    }
}
