namespace Lumine.Fairy.ContactManagement.App.ViewModels;

internal static class DelimitedTextFieldHelper
{
    public static string Join(IEnumerable<string> values)
        => string.Join("; ", values.Where(static value => !string.IsNullOrWhiteSpace(value)).Select(static value => value.Trim()));

    public static List<string> Split(string raw)
        => raw.Split([';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
