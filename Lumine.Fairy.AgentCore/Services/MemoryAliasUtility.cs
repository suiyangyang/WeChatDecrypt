using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lumine.Fairy.Services;

public static class MemoryAliasUtility
{
    private static readonly Regex AliasTokenPattern = new(@"[A-Za-z0-9]+|[\u4e00-\u9fff]+", RegexOptions.Compiled);
    private static readonly Regex NonAliasCharacterPattern = new(@"[^A-Za-z0-9\u4e00-\u9fff]+", RegexOptions.Compiled);
    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);

    public static IReadOnlyList<string> ExpandQueryTerms(string input, string currentProject)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddAliasVariants(values, currentProject);

        foreach (Match match in AliasTokenPattern.Matches(input ?? string.Empty))
        {
            var value = match.Value.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                AddAliasVariants(values, value);
            }
        }

        return values.ToList();
    }

    public static IReadOnlyList<string> BuildAliasVariants(params string[] values)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            AddAliasVariants(results, value);
        }

        return results.ToList();
    }

    public static IReadOnlyList<string> BuildAliasVariants(IEnumerable<string> values)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            AddAliasVariants(results, value);
        }

        return results.ToList();
    }

    public static string NormalizeForAliasMatch(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var withoutWhitespace = WhitespacePattern.Replace(value, string.Empty).Trim().ToLowerInvariant();
        return NonAliasCharacterPattern.Replace(withoutWhitespace, string.Empty);
    }

    public static bool ContainsAnyAlias(string source, IEnumerable<string> aliases)
    {
        var normalizedSource = NormalizeForAliasMatch(source);
        if (string.IsNullOrWhiteSpace(normalizedSource))
        {
            return false;
        }

        foreach (var alias in aliases)
        {
            var normalizedAlias = NormalizeForAliasMatch(alias);
            if (string.IsNullOrWhiteSpace(normalizedAlias))
            {
                continue;
            }

            if (normalizedSource.Contains(normalizedAlias, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddAliasVariants(HashSet<string> aliases, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        aliases.Add(trimmed);

        var normalized = NormalizeForAliasMatch(trimmed);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            aliases.Add(normalized);
        }

        var tokens = AliasTokenPattern.Matches(trimmed)
            .Select(static match => match.Value.Trim())
            .Where(IsMeaningfulAliasToken)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var token in tokens)
        {
            aliases.Add(token);
            var normalizedToken = NormalizeForAliasMatch(token);
            if (!string.IsNullOrWhiteSpace(normalizedToken))
            {
                aliases.Add(normalizedToken);
            }
        }

        if (tokens.Count > 1)
        {
            aliases.Add(string.Concat(tokens));
            var asciiWordTokens = tokens
                .Where(static token => token.All(static ch => char.IsLetterOrDigit(ch) && ch < 128))
                .ToList();
            if (asciiWordTokens.Count > 1)
            {
                var acronym = string.Concat(asciiWordTokens.Select(static token => char.ToLowerInvariant(token[0])));
                if (acronym.Length >= 2)
                {
                    aliases.Add(acronym);
                }
            }
        }
    }

    private static bool IsMeaningfulAliasToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        var isChinese = trimmed.Any(static ch => ch >= 0x4e00 && ch <= 0x9fff);
        return isChinese
            ? trimmed.Length >= 2
            : trimmed.Length >= 2;
    }
}
