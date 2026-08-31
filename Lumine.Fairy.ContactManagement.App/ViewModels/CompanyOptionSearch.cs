using TinyPinyin;

namespace Lumine.Fairy.ContactManagement.App.ViewModels;

public static class CompanyOptionSearch
{
    private static readonly IReadOnlyDictionary<char, string> PinyinOverrides = new Dictionary<char, string>
    {
        // TinyPinyin uses the colloquial "nei" for 那. Use its standard Mandarin
        // reading so 那 and 纳 compare consistently without fuzzy matching.
        ['那'] = "na"
    };

    public static bool Matches(string displayName, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        var normalizedKeyword = Normalize(keyword);
        if (Normalize(displayName).Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var pinyin = GetPinyin(displayName);
        if (ContainsStrictPinyinMatch(displayName, keyword, pinyin, normalizedKeyword))
        {
            return true;
        }

        return false;
    }

    public static LookupOptionViewModel? FindBestMatch(
        string text,
        IEnumerable<LookupOptionViewModel> companyOptions)
    {
        ArgumentNullException.ThrowIfNull(companyOptions);

        return companyOptions
            .Where(option => !string.IsNullOrWhiteSpace(option.DisplayName))
            .Select(option => new
            {
                Option = option,
                MatchLength = GetMatchLength(text, option.DisplayName)
            })
            .Where(item => item.MatchLength > 0)
            .OrderByDescending(item => item.MatchLength)
            .ThenByDescending(item => Normalize(item.Option.DisplayName).Length)
            .Select(item => item.Option)
            .FirstOrDefault();
    }

    private static int GetMatchLength(string text, string companyName)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(companyName))
        {
            return 0;
        }

        var normalizedText = Normalize(text);
        var normalizedCompanyName = Normalize(companyName);
        if (ContainsInEitherDirection(normalizedText, normalizedCompanyName))
        {
            return normalizedCompanyName.Length;
        }

        var normalizedTextPinyin = GetPinyin(text);
        var normalizedCompanyPinyin = GetPinyin(companyName);
        if (ContainsStrictPinyinMatch(text, companyName, normalizedTextPinyin, normalizedCompanyPinyin))
        {
            return normalizedCompanyPinyin.Length;
        }

        return 0;
    }

    private static string Normalize(string value)
        => string.Concat(value.Where(static character => !char.IsWhiteSpace(character)))
            .ToLowerInvariant();

    private static string GetPinyin(string value)
        => string.Concat(value
            .Where(IsPinyinRelevantCharacter)
            .Select(GetCharacterPinyin))
            .ToLowerInvariant();

    private static string GetCharacterPinyin(char character)
    {
        if (PinyinOverrides.TryGetValue(character, out var pinyin))
        {
            return pinyin;
        }

        try
        {
            return PinyinHelper.GetPinyin(character.ToString(), string.Empty);
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    private static bool IsPinyinRelevantCharacter(char character)
        => char.IsLetterOrDigit(character)
            || character is >= '\u3400' and <= '\u9FFF';

    private static bool ContainsStrictPinyinMatch(
        string firstText,
        string secondText,
        string firstPinyin,
        string secondPinyin)
    {
        if (IsChineseText(firstText) && IsChineseText(secondText))
        {
            return ContainsPinyinSequenceInEitherDirection(
                GetPinyinSyllables(firstText),
                GetPinyinSyllables(secondText));
        }

        return ContainsInEitherDirection(firstPinyin, secondPinyin);
    }

    private static IReadOnlyList<string> GetPinyinSyllables(string value)
        => value
            .Where(ContainsChineseCharacter)
            .Select(GetCharacterPinyin)
            .Where(static pinyin => !string.IsNullOrWhiteSpace(pinyin))
            .Select(static pinyin => pinyin.ToLowerInvariant())
            .ToArray();

    private static bool ContainsPinyinSequenceInEitherDirection(
        IReadOnlyList<string> first,
        IReadOnlyList<string> second)
        => ContainsPinyinSequence(first, second)
            || ContainsPinyinSequence(second, first);

    private static bool ContainsPinyinSequence(
        IReadOnlyList<string> source,
        IReadOnlyList<string> target)
    {
        if (source.Count == 0 || target.Count == 0 || target.Count > source.Count)
        {
            return false;
        }

        for (var start = 0; start <= source.Count - target.Count; start++)
        {
            var matches = true;
            for (var index = 0; index < target.Count; index++)
            {
                if (!string.Equals(source[start + index], target[index], StringComparison.OrdinalIgnoreCase))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsChineseCharacter(string value)
        => value.Any(ContainsChineseCharacter);

    private static bool IsChineseText(string value)
        => value.All(static character => !char.IsLetterOrDigit(character) || ContainsChineseCharacter(character));

    private static bool ContainsChineseCharacter(char character)
        => character is >= '\u3400' and <= '\u9FFF';

    private static bool ContainsInEitherDirection(string first, string second)
        => first.Contains(second, StringComparison.OrdinalIgnoreCase)
            || second.Contains(first, StringComparison.OrdinalIgnoreCase);
}
