namespace Lumine.Fairy.TodoList.App.ViewModels;

internal static class MultilineTextFieldHelper
{
    public const int TabDisplayWidth = 4;

    private const char IdeographicSpace = '\u3000';
    private const char FigureSpace = '\u2007';

    public static string Join(IEnumerable<string> values)
        => string.Join(Environment.NewLine, values.Where(static value => !string.IsNullOrWhiteSpace(value)).Select(static value => value.Trim()));

    public static List<string> Split(string raw)
        => raw.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToList();

    public static string ConvertTabsToAlignedWhitespace(string rawText)
    {
        if (string.IsNullOrEmpty(rawText) || !rawText.Contains('\t'))
        {
            return rawText;
        }

        var normalizedText = rawText.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalizedText.Split('\n');
        var convertedLines = new string[lines.Length];

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            var builder = new System.Text.StringBuilder(line.Length);
            var displayColumn = 0;

            foreach (var character in line)
            {
                if (character == '\t')
                {
                    var paddingWidth = GetTabPaddingWidth(displayColumn);
                    builder.Append(BuildAlignmentWhitespace(paddingWidth));
                    displayColumn += paddingWidth;
                    continue;
                }

                builder.Append(character);
                displayColumn += GetCharacterDisplayWidth(character);
            }

            convertedLines[lineIndex] = builder.ToString();
        }

        return string.Join(Environment.NewLine, convertedLines);
    }

    public static string BuildAlignmentWhitespace(int displayWidth)
    {
        if (displayWidth <= 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(displayWidth);
        var remainingWidth = displayWidth;

        while (remainingWidth >= 2)
        {
            builder.Append(IdeographicSpace);
            remainingWidth -= 2;
        }

        if (remainingWidth == 1)
        {
            builder.Append(FigureSpace);
        }

        return builder.ToString();
    }

    public static int GetTabPaddingWidth(int currentDisplayColumn)
    {
        var paddingWidth = TabDisplayWidth - (currentDisplayColumn % TabDisplayWidth);
        return paddingWidth <= 0 ? TabDisplayWidth : paddingWidth;
    }

    public static int GetDisplayColumn(string text)
    {
        var column = 0;

        foreach (var character in text)
        {
            if (character == '\t')
            {
                column += GetTabPaddingWidth(column);
                continue;
            }

            column += GetCharacterDisplayWidth(character);
        }

        return column;
    }

    private static int GetCharacterDisplayWidth(char character)
    {
        if (character is '\r' or '\n' || char.IsControl(character))
        {
            return 0;
        }

        if (character == IdeographicSpace)
        {
            return 2;
        }

        if (character == FigureSpace)
        {
            return 1;
        }

        return IsWideCharacter(character) ? 2 : 1;
    }

    private static bool IsWideCharacter(char character)
    {
        return character switch
        {
            >= '\u1100' and <= '\u115F' => true,
            >= '\u2E80' and <= '\uA4CF' => true,
            >= '\uAC00' and <= '\uD7A3' => true,
            >= '\uF900' and <= '\uFAFF' => true,
            >= '\uFE10' and <= '\uFE19' => true,
            >= '\uFE30' and <= '\uFE6F' => true,
            >= '\uFF00' and <= '\uFF60' => true,
            >= '\uFFE0' and <= '\uFFE6' => true,
            _ => false
        };
    }
}
