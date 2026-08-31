using Lumine.Fairy.ContactManagement.Core.Models;
using Lumine.Fairy.ContactManagement.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Lumine.Fairy.ContactManagement.Core.Services;

public sealed partial class ScratchNoteParseService
{
    public ScratchNoteParseResult Parse(string rawText, DateOnly? fallbackDate = null)
    {
        var result = new ScratchNoteParseResult
        {
            WorkDate = fallbackDate
        };

        if (string.IsNullOrWhiteSpace(rawText))
        {
            return result;
        }

        var lines = rawText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        ClientIssueEntry? currentIssue = null;
        DateOnly? currentDate = fallbackDate;
        var inProjectBacklogSection = false;
        string? currentProjectHeading = null;
        TodoEntry? lastTodoEntry = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                if (currentIssue is not null)
                {
                    CommitClientIssue(result.ClientIssues, ref currentIssue);
                }

                if (!inProjectBacklogSection)
                {
                    currentProjectHeading = null;
                }

                continue;
            }

            if (line.Equals("每日 Todo", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("每日 Todo：", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("每日 Todo:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryParseDate(line, out var parsedDate))
            {
                currentDate = parsedDate;
                result.WorkDate = parsedDate;
                inProjectBacklogSection = false;
                currentProjectHeading = null;
                lastTodoEntry = null;
                CommitClientIssue(result.ClientIssues, ref currentIssue);
                continue;
            }

            if (IsSeparatorLine(line))
            {
                CommitClientIssue(result.ClientIssues, ref currentIssue);
                inProjectBacklogSection = true;
                currentProjectHeading = null;
                lastTodoEntry = null;
                continue;
            }

            if (TryStartClientIssue(line, currentDate, ref currentIssue))
            {
                continue;
            }

            if (TryAppendClientIssueLine(line, currentIssue))
            {
                continue;
            }

            CommitClientIssue(result.ClientIssues, ref currentIssue);

            if (TryAppendIndentedDetail(line, lastTodoEntry))
            {
                continue;
            }

            if (TryParseDailyTodoLine(line, currentDate, out var dailyTodoEntry))
            {
                result.TodoEntries.Add(dailyTodoEntry);
                lastTodoEntry = dailyTodoEntry;
                continue;
            }

            if (TryParseProjectHeading(line, inProjectBacklogSection, out var projectHeading))
            {
                currentProjectHeading = projectHeading;
                lastTodoEntry = null;
                continue;
            }

            if (TryParseProjectBacklogTodoLine(line, currentProjectHeading, out var backlogTodoEntry))
            {
                result.TodoEntries.Add(backlogTodoEntry);
                lastTodoEntry = backlogTodoEntry;
                continue;
            }

            result.UnclassifiedFragments.Add(new ScratchNoteUnclassifiedFragment { Text = line });
            lastTodoEntry = null;
        }

        CommitClientIssue(result.ClientIssues, ref currentIssue);
        return result;
    }

    private static bool TryStartClientIssue(string line, DateOnly? currentDate, ref ClientIssueEntry? currentIssue)
    {
        var match = ClientIssueHeaderRegex().Match(line);
        if (!match.Success)
        {
            return false;
        }

        var name = match.Groups[1].Value.Trim();
        currentIssue = new ClientIssueEntry
        {
            WorkDate = currentDate,
            ProjectName = name,
            ClientName = name
        };
        return true;
    }

    private static bool TryAppendClientIssueLine(string line, ClientIssueEntry? currentIssue)
    {
        if (currentIssue is null)
        {
            return false;
        }

        var issueMatch = ClientIssueLineRegex().Match(line);
        if (issueMatch.Success)
        {
            currentIssue.IssueLines.Add(issueMatch.Groups[1].Value.Trim());
            return true;
        }

        var solutionMatch = SolutionLineRegex().Match(line);
        if (solutionMatch.Success)
        {
            currentIssue.SolutionLines.Add(solutionMatch.Groups[1].Value.Trim());
            return true;
        }

        return false;
    }

    private static bool TryParseDailyTodoLine(string line, DateOnly? currentDate, out TodoEntry todoEntry)
    {
        var match = DailyTodoLineRegex().Match(line);
        if (!match.Success)
        {
            todoEntry = null!;
            return false;
        }

        var parsedContent = ParseTodoContent(match.Groups[3].Value, allowHoursAtEnd: true);

        todoEntry = new TodoEntry
        {
            WorkDate = currentDate,
            ProjectName = match.Groups[2].Value.Trim(),
            Title = parsedContent.Content,
            Content = parsedContent.Content,
            Status = parsedContent.Status,
            WorkHours = parsedContent.WorkHours,
            IsDaily = currentDate.HasValue,
            IsFromScratchNote = true,
            SourceText = line,
            Notes = parsedContent.Notes,
            SortOrder = int.TryParse(match.Groups[1].Value, out var sortOrder) ? sortOrder : 0
        };
        return true;
    }

    private static bool TryParseProjectBacklogTodoLine(string line, string? currentProjectHeading, out TodoEntry todoEntry)
    {
        var match = BacklogTodoLineRegex().Match(line);
        if (!match.Success || string.IsNullOrWhiteSpace(currentProjectHeading))
        {
            todoEntry = null!;
            return false;
        }

        var parsedContent = ParseTodoContent(match.Groups[2].Value, allowHoursAtEnd: false);
        todoEntry = new TodoEntry
        {
            ProjectName = currentProjectHeading,
            Title = parsedContent.Content,
            Content = parsedContent.Content,
            Status = parsedContent.Status,
            WorkHours = parsedContent.WorkHours,
            IsDaily = false,
            IsFromScratchNote = true,
            SourceText = line,
            Notes = parsedContent.Notes,
            SortOrder = int.TryParse(match.Groups[1].Value, out var sortOrder) ? sortOrder : 0
        };
        return true;
    }

    private static bool TryParseProjectHeading(string line, bool inProjectBacklogSection, out string projectHeading)
    {
        var normalized = line.Trim();
        if (!inProjectBacklogSection || string.IsNullOrWhiteSpace(normalized))
        {
            projectHeading = string.Empty;
            return false;
        }

        if (normalized.Contains(':', StringComparison.Ordinal) || normalized.Contains('：', StringComparison.Ordinal))
        {
            projectHeading = string.Empty;
            return false;
        }

        if (BacklogTodoLineRegex().IsMatch(normalized) || IndentedDetailRegex().IsMatch(normalized))
        {
            projectHeading = string.Empty;
            return false;
        }

        projectHeading = NormalizeProjectHeading(normalized);
        return !string.IsNullOrWhiteSpace(projectHeading);
    }

    private static bool TryAppendIndentedDetail(string line, TodoEntry? lastTodoEntry)
    {
        if (lastTodoEntry is null)
        {
            return false;
        }

        var match = IndentedDetailRegex().Match(line);
        if (!match.Success)
        {
            return false;
        }

        var detailText = match.Groups[1].Value.Trim();
        if (string.IsNullOrWhiteSpace(detailText))
        {
            return false;
        }

        lastTodoEntry.Notes = string.IsNullOrWhiteSpace(lastTodoEntry.Notes)
            ? detailText
            : $"{lastTodoEntry.Notes}{Environment.NewLine}{detailText}";
        return true;
    }

    private static bool IsSeparatorLine(string line)
        => SeparatorLineRegex().IsMatch(line);

    private static string NormalizeProjectHeading(string heading)
    {
        var normalized = heading.Trim();
        normalized = Regex.Replace(normalized, @"剩余功能$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
        return normalized;
    }

    private static ParsedTodoContent ParseTodoContent(string rawContent, bool allowHoursAtEnd)
    {
        var content = rawContent.Trim();
        var status = TodoStatus.NotStarted;
        var notes = string.Empty;
        decimal? workHours = null;

        if (allowHoursAtEnd)
        {
            var hoursMatch = TrailingHoursRegex().Match(content);
            if (hoursMatch.Success)
            {
                var hoursText = hoursMatch.Groups[1].Value.Trim();
                if (TryParseWorkHours(hoursText, out var parsedHours))
                {
                    workHours = parsedHours;
                    content = content[..hoursMatch.Index].TrimEnd();
                }
            }
        }

        status = DetectStatus(content, out var statusToken);
        if (!string.IsNullOrWhiteSpace(statusToken))
        {
            content = content.Replace(statusToken, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        }

        if (content.Contains("是否需要做", StringComparison.OrdinalIgnoreCase))
        {
            notes = content[(content.IndexOf("是否需要做", StringComparison.OrdinalIgnoreCase))..].Trim();
            content = content[..content.IndexOf("是否需要做", StringComparison.OrdinalIgnoreCase)].Trim();
        }

        content = content.Trim('—', '-', '–', ' ', '\t');
        return new ParsedTodoContent(content, workHours, status, notes);
    }

    private static bool TryParseWorkHours(string text, out decimal workHours)
    {
        if (decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out workHours))
        {
            return true;
        }

        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out workHours);
    }

    private static TodoStatus DetectStatus(string text, out string statusToken)
    {
        if (text.Contains("√", StringComparison.Ordinal) || text.Contains("完成", StringComparison.OrdinalIgnoreCase))
        {
            statusToken = text.Contains("√", StringComparison.Ordinal) ? "√" : "完成";
            return TodoStatus.Completed;
        }

        if (text.Contains("←", StringComparison.Ordinal))
        {
            statusToken = "←";
            return TodoStatus.InProgress;
        }

        if (text.Contains("Block", StringComparison.OrdinalIgnoreCase))
        {
            statusToken = Regex.Match(text, "Block", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Value;
            return TodoStatus.Blocked;
        }

        if (text.Contains("待定", StringComparison.OrdinalIgnoreCase))
        {
            statusToken = "待定";
            return TodoStatus.Blocked;
        }

        if (text.Contains("未开始", StringComparison.OrdinalIgnoreCase))
        {
            statusToken = "未开始";
            return TodoStatus.NotStarted;
        }

        if (text.Contains("进行中", StringComparison.OrdinalIgnoreCase))
        {
            statusToken = "进行中";
            return TodoStatus.InProgress;
        }

        statusToken = string.Empty;
        return TodoStatus.NotStarted;
    }

    private static bool TryParseDate(string line, out DateOnly date)
    {
        var match = DateLineRegex().Match(line);
        if (!match.Success)
        {
            date = default;
            return false;
        }

        var yearText = match.Groups[1].Value;
        var month = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var day = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        var year = string.IsNullOrWhiteSpace(yearText) ? DateTime.Today.Year : int.Parse(yearText, CultureInfo.InvariantCulture);
        date = new DateOnly(year, month, day);
        return true;
    }

    private static void CommitClientIssue(List<ClientIssueEntry> clientIssues, ref ClientIssueEntry? currentIssue)
    {
        if (currentIssue is null)
        {
            return;
        }

        if (currentIssue.IssueLines.Count > 0 || currentIssue.SolutionLines.Count > 0)
        {
            clientIssues.Add(currentIssue);
        }

        currentIssue = null;
    }

    [GeneratedRegex(@"^(?:(\d{4})/)?(\d{1,2})/(\d{1,2})$")]
    private static partial Regex DateLineRegex();

    [GeneratedRegex(@"^(\d+)\s*[\.、]\s*([^:：]+)\s*[:：]\s*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DailyTodoLineRegex();

    [GeneratedRegex(@"^(\d+)\s*[\.、]\s*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex BacklogTodoLineRegex();

    [GeneratedRegex(@"^\|--\s*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex IndentedDetailRegex();

    [GeneratedRegex(@"^={5,}$")]
    private static partial Regex SeparatorLineRegex();

    [GeneratedRegex(@"\s+([0-9]+(?:\.[0-9]+)?)$")]
    private static partial Regex TrailingHoursRegex();

    [GeneratedRegex(@"^(?:客户/项目|客户|项目)\s*[:：]\s*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ClientIssueHeaderRegex();

    [GeneratedRegex(@"^问题\s*[:：]\s*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ClientIssueLineRegex();

    [GeneratedRegex(@"^方案\s*[:：]\s*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SolutionLineRegex();

    private sealed record ParsedTodoContent(string Content, decimal? WorkHours, TodoStatus Status, string Notes);
}
