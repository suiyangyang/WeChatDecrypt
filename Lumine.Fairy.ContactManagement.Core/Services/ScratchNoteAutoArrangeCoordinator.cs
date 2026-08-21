using Lumine.Fairy.ContactManagement.Core.Models;
using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.Core.Services;

public sealed class ScratchNoteAutoArrangeCoordinator
{
    public static readonly TimeSpan DefaultRealtimeDebounce = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan DefaultStableArrangeIdle = TimeSpan.FromMinutes(1);

    private readonly ScratchNoteParseService _parseService;
    private readonly DailyReportFormatter _formatter;

    public ScratchNoteAutoArrangeCoordinator(ScratchNoteParseService? parseService = null, DailyReportFormatter? formatter = null)
    {
        _parseService = parseService ?? new ScratchNoteParseService();
        _formatter = formatter ?? new DailyReportFormatter();
    }

    public ScratchNoteAutoArrangeResult Arrange(ScratchNoteDocument document, DateOnly? fallbackDate = null, DateTimeOffset? arrangedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var parseResult = _parseService.Parse(document.RawText, document.WorkDate ?? fallbackDate);
        var reportDate = parseResult.WorkDate ?? document.WorkDate ?? fallbackDate ?? DateOnly.FromDateTime(DateTime.Today);

        foreach (var todo in parseResult.TodoEntries)
        {
            if (todo.IsDaily)
            {
                todo.WorkDate ??= reportDate;
            }

            todo.IsFromScratchNote = true;
        }

        foreach (var issue in parseResult.ClientIssues)
        {
            issue.WorkDate ??= reportDate;
        }

        var reportItems = parseResult.TodoEntries
            .Where(item => item.IsDaily && item.WorkDate == reportDate)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var dailyReport = new DailyReport
        {
            Date = reportDate,
            Items = reportItems,
            ClientIssues = parseResult.ClientIssues
                .Where(item => item.WorkDate == reportDate)
                .ToList(),
            TotalHours = reportItems.Sum(item => item.WorkHours ?? 0m),
            RawScratchText = document.RawText,
            FormattedReportText = _formatter.Format(reportItems)
        };

        return new ScratchNoteAutoArrangeResult
        {
            Document = new ScratchNoteDocument
            {
                Id = document.Id,
                WorkDate = reportDate,
                Title = document.Title,
                RawText = document.RawText,
                CreatedAtUtc = document.CreatedAtUtc,
                UpdatedAtUtc = document.UpdatedAtUtc,
                LastArrangedAtUtc = arrangedAtUtc ?? DateTimeOffset.UtcNow
            },
            ParseResult = parseResult,
            DailyReport = dailyReport
        };
    }

    public ScratchNoteArrangeDecision GetArrangeDecision(
        ScratchNoteArrangeTrigger trigger,
        DateTimeOffset now,
        DateTimeOffset? lastEditedAtUtc,
        DateTimeOffset? lastArrangedAtUtc,
        TimeSpan? realtimeDebounce = null,
        TimeSpan? stableArrangeIdle = null)
    {
        var debounce = realtimeDebounce ?? DefaultRealtimeDebounce;
        var idleThreshold = stableArrangeIdle ?? DefaultStableArrangeIdle;

        return trigger switch
        {
            ScratchNoteArrangeTrigger.Manual => new ScratchNoteArrangeDecision
            {
                ShouldArrange = true,
                Depth = ScratchNoteArrangeDepth.Full,
                Reason = "manual-request"
            },
            ScratchNoteArrangeTrigger.RealtimeEdit => BuildRealtimeDecision(now, lastEditedAtUtc, debounce),
            ScratchNoteArrangeTrigger.IdleTimeout => BuildIdleDecision(now, lastEditedAtUtc, lastArrangedAtUtc, idleThreshold),
            _ => new ScratchNoteArrangeDecision()
        };
    }

    private static ScratchNoteArrangeDecision BuildRealtimeDecision(DateTimeOffset now, DateTimeOffset? lastEditedAtUtc, TimeSpan debounce)
    {
        if (!lastEditedAtUtc.HasValue)
        {
            return new ScratchNoteArrangeDecision
            {
                ShouldArrange = false,
                Depth = ScratchNoteArrangeDepth.None,
                RecommendedDelay = debounce,
                Reason = "awaiting-first-edit"
            };
        }

        var elapsed = now - lastEditedAtUtc.Value;
        if (elapsed < debounce)
        {
            return new ScratchNoteArrangeDecision
            {
                ShouldArrange = false,
                Depth = ScratchNoteArrangeDepth.None,
                RecommendedDelay = debounce - elapsed,
                Reason = "debounce-pending"
            };
        }

        return new ScratchNoteArrangeDecision
        {
            ShouldArrange = true,
            Depth = ScratchNoteArrangeDepth.Lightweight,
            Reason = "realtime-debounce-elapsed"
        };
    }

    private static ScratchNoteArrangeDecision BuildIdleDecision(DateTimeOffset now, DateTimeOffset? lastEditedAtUtc, DateTimeOffset? lastArrangedAtUtc, TimeSpan idleThreshold)
    {
        if (!lastEditedAtUtc.HasValue)
        {
            return new ScratchNoteArrangeDecision
            {
                ShouldArrange = false,
                Depth = ScratchNoteArrangeDepth.None,
                Reason = "no-edit-yet"
            };
        }

        var elapsed = now - lastEditedAtUtc.Value;
        if (elapsed < idleThreshold)
        {
            return new ScratchNoteArrangeDecision
            {
                ShouldArrange = false,
                Depth = ScratchNoteArrangeDepth.None,
                RecommendedDelay = idleThreshold - elapsed,
                Reason = "idle-threshold-not-reached"
            };
        }

        if (lastArrangedAtUtc.HasValue && lastArrangedAtUtc.Value >= lastEditedAtUtc.Value)
        {
            return new ScratchNoteArrangeDecision
            {
                ShouldArrange = false,
                Depth = ScratchNoteArrangeDepth.None,
                Reason = "already-arranged-after-last-edit"
            };
        }

        return new ScratchNoteArrangeDecision
        {
            ShouldArrange = true,
            Depth = ScratchNoteArrangeDepth.Full,
            Reason = "idle-threshold-reached"
        };
    }
}