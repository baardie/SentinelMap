namespace SentinelMap.Infrastructure.Correlation;

/// <summary>
/// Pure Jaro-Winkler string similarity algorithm. Returns 0.0 (no match) to 1.0 (exact match).
/// </summary>
public static class JaroWinkler
{
    private const double WinklerScalingFactor = 0.1;
    private const int MaxPrefixLength = 4;

    public static double Similarity(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2))
            return 1.0;

        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
            return 0.0;

        if (s1 == s2)
            return 1.0;

        var jaro = JaroSimilarity(s1, s2);

        // Winkler adjustment: boost score for common prefix up to 4 chars
        var prefixLength = 0;
        var maxPrefix = Math.Min(MaxPrefixLength, Math.Min(s1.Length, s2.Length));
        for (var i = 0; i < maxPrefix; i++)
        {
            if (s1[i] == s2[i])
                prefixLength++;
            else
                break;
        }

        return jaro + prefixLength * WinklerScalingFactor * (1.0 - jaro);
    }

    private static double JaroSimilarity(string s1, string s2)
    {
        var len1 = s1.Length;
        var len2 = s2.Length;

        var matchWindow = Math.Max(len1, len2) / 2 - 1;
        if (matchWindow < 0) matchWindow = 0;

        var s1Matched = new bool[len1];
        var s2Matched = new bool[len2];

        var matches = 0;
        var transpositions = 0;

        // Count matches
        for (var i = 0; i < len1; i++)
        {
            var start = Math.Max(0, i - matchWindow);
            var end = Math.Min(i + matchWindow + 1, len2);

            for (var j = start; j < end; j++)
            {
                if (s2Matched[j] || s1[i] != s2[j])
                    continue;

                s1Matched[i] = true;
                s2Matched[j] = true;
                matches++;
                break;
            }
        }

        if (matches == 0)
            return 0.0;

        // Count transpositions
        var k = 0;
        for (var i = 0; i < len1; i++)
        {
            if (!s1Matched[i]) continue;

            while (!s2Matched[k])
                k++;

            if (s1[i] != s2[k])
                transpositions++;

            k++;
        }

        return ((double)matches / len1
              + (double)matches / len2
              + (matches - transpositions / 2.0) / matches) / 3.0;
    }
}
