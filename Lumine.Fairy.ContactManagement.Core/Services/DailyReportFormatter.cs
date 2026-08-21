using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.Core.Services;

public sealed class DailyReportFormatter
{
    public string Format(DailyReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return Format(report.Items);
    }

    public string Format(IEnumerable<TodoEntry> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var lines = items
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select((item, index) => FormatLine(index + 1, item))
            .ToList();

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatLine(int index, TodoEntry item)
    {
        var projectName = NormalizeProjectName(item.ProjectName);
        var content = ResolveContent(item);
        var hours = item.WorkHours ?? 0m;
        return $"{index}、({projectName}){content},{FormatHours(hours)};";
    }

    private static string ResolveContent(TodoEntry item)
    {
        var title = item.Title.Trim();
        var content = item.Content.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            return content;
        }

        if (string.IsNullOrWhiteSpace(content) || string.Equals(title, content, StringComparison.Ordinal))
        {
            return title;
        }

        return $"{title} {content}".Trim();
    }

    private static string NormalizeProjectName(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.StartsWith('(') && normalized.EndsWith(')') && normalized.Length >= 2)
        {
            normalized = normalized[1..^1].Trim();
        }

        if (normalized.StartsWith('（') && normalized.EndsWith('）') && normalized.Length >= 2)
        {
            normalized = normalized[1..^1].Trim();
        }

        return string.IsNullOrWhiteSpace(normalized) ? "其他" : normalized;
    }

    private static string FormatHours(decimal value)
        => value == decimal.Truncate(value)
            ? decimal.Truncate(value).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}