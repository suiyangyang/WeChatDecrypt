using Lumine.Fairy.Protocol;

namespace Lumine.Fairy.Application;

public static class WeChatAnalysisSelection
{
    public const int UnanalyzedMessageReadLimit = 200;
    public const int DefaultMessageReadLimit = 50;

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

        var threshold = mode switch
        {
            WeChatAutomaticAnalysisMode.CurrentWeek => StartOfCurrentWeekUtc(),
            WeChatAutomaticAnalysisMode.OneMonth => DateTimeOffset.UtcNow.AddMonths(-1),
            _ => DateTimeOffset.MinValue,
        };

        var eligible = messages
            .Where(message => ToDateTimeOffset(message.Timestamp) >= threshold)
            .ToList();
        if (string.IsNullOrWhiteSpace(previousCursor))
            return mode == WeChatAutomaticAnalysisMode.Unanalyzed ? [] : eligible;

        var previousIndex = FindCursorIndex(messages, previousCursor);
        if (previousIndex >= 0)
            return messages
                .Select((message, index) => (message, index))
                .Where(item => item.index > previousIndex && ToDateTimeOffset(item.message.Timestamp) >= threshold)
                .Select(item => item.message)
                .ToList();

        // The API window is bounded. When the exact cursor has fallen out of
        // that window, its timestamp is the only safe boundary available.
        // This intentionally processes only the returned window; older excess
        // messages are not backfilled in Unanalyzed mode.
        var previousTimestamp = GetCursorTimestamp(previousCursor);
        return previousTimestamp is null
            ? []
            : eligible.Where(message => ToDateTimeOffset(message.Timestamp) > previousTimestamp.Value).ToList();
    }

    public static string GetCursor(ChatMessage message)
        => $"{message.Id}:{message.Timestamp}";

    public static bool HasCursor(IReadOnlyList<ChatMessage> messages, string? cursor)
        => FindCursorIndex(messages, cursor) >= 0;

    private static int FindCursorIndex(IReadOnlyList<ChatMessage> messages, string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return -1;
        var separator = cursor.LastIndexOf(':');
        var previousId = separator > 0 ? cursor[..separator] : cursor;
        return messages.ToList().FindIndex(x => x.Id == previousId);
    }

    private static DateTimeOffset? GetCursorTimestamp(string cursor)
    {
        var separator = cursor.LastIndexOf(':');
        if (separator < 0 || !long.TryParse(cursor[(separator + 1)..], out var timestamp)) return null;
        return ToDateTimeOffset(timestamp);
    }

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
