namespace ManuHub.Memora.Helpers;

internal static class CommonHelper
{
    /// <summary>
    /// Simple glob-like pattern match (Redis KEYS style)
    /// Supports: * (any chars), ? (single char)
    /// </summary>
    public static bool MatchesPattern(string key, string pattern)
    {
        if (pattern == "*") return true;

        int i = 0, j = 0;
        while (i < key.Length && j < pattern.Length)
        {
            char p = pattern[j];

            if (p == '*')
            {
                // * matches zero or more chars
                if (j + 1 == pattern.Length) return true; // * at end matches rest
                j++;
                while (i < key.Length && key[i] != pattern[j])
                    i++;
            }
            else if (p == '?')
            {
                // ? matches exactly one char
                i++;
                j++;
            }
            else if (p == key[i])
            {
                i++;
                j++;
            }
            else
            {
                return false;
            }
        }

        // If pattern ends with *, it matches
        if (j < pattern.Length && pattern[j] == '*' && j + 1 == pattern.Length)
            return true;

        return i == key.Length && j == pattern.Length;
    }
}
