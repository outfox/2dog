namespace twodog.cli;

/// <summary>"Did you mean" lookups for mistyped verbs and options.</summary>
internal static class Suggest
{
    /// <summary>
    /// The candidate closest to the input when it is close enough to be a typo: a unique prefix match, or an edit
    /// distance of at most two (one for short inputs). Null when nothing is convincing.
    /// </summary>
    public static string? Closest(string input, IEnumerable<string> candidates)
    {
        var list = candidates.Distinct().ToList();
        var prefixed = list.Where(c => c.StartsWith(input, StringComparison.OrdinalIgnoreCase)).ToList();
        if (prefixed.Count == 1 && input.Length >= 3) return prefixed[0];

        var limit = input.Length <= 4 ? 1 : 2;
        var best = list
            .Select(c => (Candidate: c, Distance: Distance(input.ToLowerInvariant(), c.ToLowerInvariant())))
            .Where(c => c.Distance <= limit)
            .OrderBy(c => c.Distance).ThenBy(c => c.Candidate.Length)
            .FirstOrDefault();
        return best.Candidate;
    }

    /// <summary>Damerau-Levenshtein distance (insert, delete, substitute, transpose adjacent).</summary>
    internal static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        for (var j = 1; j <= b.Length; j++)
        {
            var cost = a[i - 1] == b[j - 1] ? 0 : 1;
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
        }

        return d[a.Length, b.Length];
    }
}
