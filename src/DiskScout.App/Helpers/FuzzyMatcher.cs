namespace DiskScout.Helpers;

public static class FuzzyMatcher
{
    private const double ExactMatchConfidence = 1.0;
    private const double PrefixMatchConfidence = 0.95;

    public static double ComputeMatch(string folderName, string? publisher, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return 0;

        var folderTokens = Tokenize(folderName);
        if (folderTokens.Count == 0) return 0;

        foreach (var candidate in EnumerateCandidates(publisher, displayName))
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;

            if (string.Equals(folderName, candidate, StringComparison.OrdinalIgnoreCase))
                return ExactMatchConfidence;

            if (folderName.StartsWith(candidate, StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith(folderName, StringComparison.OrdinalIgnoreCase))
                return PrefixMatchConfidence;

            if (candidate.Contains(folderName, StringComparison.OrdinalIgnoreCase) ||
                folderName.Contains(candidate, StringComparison.OrdinalIgnoreCase))
                return PrefixMatchConfidence - 0.05;
        }

        var allTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in EnumerateCandidates(publisher, displayName))
        {
            foreach (var token in Tokenize(candidate))
            {
                allTokens.Add(token);
            }
        }

        if (allTokens.Count == 0) return 0;

        int intersection = 0;
        foreach (var token in folderTokens)
        {
            if (allTokens.Contains(token)) intersection++;
        }

        if (intersection < 2) return 0;

        var union = folderTokens.Count + allTokens.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    public static bool IsMatch(string folderName, string? publisher, string? displayName, double threshold = 0.7)
    {
        return ComputeMatch(folderName, publisher, displayName) >= threshold;
    }

    private static HashSet<string> Tokenize(string? input)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(input)) return set;

        var buffer = new System.Text.StringBuilder();
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c))
            {
                buffer.Append(char.ToLowerInvariant(c));
            }
            else if (buffer.Length > 0)
            {
                if (buffer.Length >= 2) set.Add(buffer.ToString());
                buffer.Clear();
            }
        }
        if (buffer.Length >= 2) set.Add(buffer.ToString());
        return set;
    }

    private static IEnumerable<string?> EnumerateCandidates(string? publisher, string? displayName)
    {
        yield return displayName;
        yield return publisher;
        if (!string.IsNullOrWhiteSpace(publisher) && !string.IsNullOrWhiteSpace(displayName))
            yield return publisher + " " + displayName;
    }
}
