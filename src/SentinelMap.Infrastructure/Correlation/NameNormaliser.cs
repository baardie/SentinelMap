using System.Text.RegularExpressions;

namespace SentinelMap.Infrastructure.Correlation;

/// <summary>
/// Normalises vessel and aircraft names for fuzzy matching.
/// </summary>
public static partial class NameNormaliser
{
    private static readonly string[] Prefixes = ["MV", "MT", "HMS", "SS", "RV", "SV", "FV", "MY"];

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultipleSpaces();

    public static string Normalise(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var normalised = name.ToUpperInvariant().Trim();

        // Strip common vessel/aircraft prefixes (only if followed by a space)
        foreach (var prefix in Prefixes)
        {
            if (normalised.StartsWith(prefix + " ", StringComparison.Ordinal))
            {
                normalised = normalised[(prefix.Length + 1)..];
                break; // Only strip one prefix
            }
        }

        // Collapse multiple whitespace to single space
        normalised = MultipleSpaces().Replace(normalised, " ");

        return normalised.Trim();
    }
}
