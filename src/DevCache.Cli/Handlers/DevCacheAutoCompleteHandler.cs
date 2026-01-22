namespace DevCache.Cli.Handlers;

internal class DevCacheAutoCompleteHandler : IAutoCompleteHandler
{
    // Separators for splitting words (space is primary for Redis-style commands)
    public char[] Separators { get; set; } = new char[] { ' ' };

    /// <summary>
    /// Called when user presses Tab. Returns suggestions based on current input.
    /// </summary>
    public string[]? GetSuggestions(string text, int index)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Split the current line into tokens
        var tokens = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return null;

        string currentToken = tokens[^1]; // last token (being typed)
        string commandPrefix = tokens[0].ToUpperInvariant(); // first token = command

        // Case 1: User is typing the command name itself
        if (tokens.Length == 1 && !text.EndsWith(" "))
        {
            return GetAllSupportedCommands()
                .Where(c => c.StartsWith(currentToken, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c)
                .ToArray();
        }

        // Case 2: User is typing arguments after a known command
        return commandPrefix switch
        {
            "INFO" => SuggestInfoSections(currentToken),
            "CONFIG" => SuggestConfigSubcommands(tokens, currentToken),
            "KEYS" => SuggestKeysPatterns(currentToken),
            "SET" or "GET" or "DEL" or "EXISTS" or "EXPIRE" or "PEXPIRE" or "TTL" or "PTTL" or "GETMETA" or "TYPE"
                       => SuggestKeys(currentToken),
            "LPUSH" or "RPUSH" or "HSET" or "HGET" or "HDEL" or "HKEYS" or "HVALS"
                       => SuggestKeysOrFields(commandPrefix, currentToken, tokens),
            "INCR" or "DECR" or "INCRBY"
                       => SuggestCounters(currentToken),
            _ => null // no suggestions for unknown commands
        };
    }

    // ────────────────────────────────────────────────
    // Helpers: Command list (keep in sync with CommandRegistry)
    // ────────────────────────────────────────────────
    private static IEnumerable<string> GetAllSupportedCommands()
    {
        yield return "PING";
        yield return "ECHO";
        yield return "INFO";
        yield return "CONFIG";
        yield return "ROLE";
        yield return "CLIENT";
        yield return "SET";
        yield return "GET";
        yield return "DEL";
        yield return "EXISTS";
        yield return "INCR";
        yield return "DECR";
        yield return "INCRBY";
        yield return "EXPIRE";
        yield return "TTL";
        yield return "PEXPIRE";
        yield return "PTTL";
        yield return "FLUSHDB";
        yield return "KEYS";
        yield return "TYPE";
        yield return "GETMETA";
        yield return "LPUSH";
        yield return "RPUSH";
        yield return "LPOP";
        yield return "RPOP";
        yield return "LLEN";
        yield return "HSET";
        yield return "HGET";
        yield return "HDEL";
        yield return "HLEN";
        yield return "HKEYS";
        yield return "HVALS";
    }

    // ────────────────────────────────────────────────
    // Context-aware suggestions
    // ────────────────────────────────────────────────

    private static string[] SuggestInfoSections(string currentToken)
    {
        var sections = new[] { "all", "server", "memory", "keyspace", "stats", "persistence", "replication", "clients" };
        return sections
            .Where(s => s.StartsWith(currentToken, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static string[] SuggestConfigSubcommands(IReadOnlyList<string> tokens, string currentToken)
    {
        if (tokens.Count == 1) // after "CONFIG"
        {
            return new[] { "GET" }
                .Where(s => s.StartsWith(currentToken, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (tokens.Count >= 2 && tokens[1].Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            // Suggest common config keys
            var commonKeys = new[]
            {
                "port", "bind", "maxmemory", "maxmemory-policy", "timeout",
                "databases", "appendonly", "requirepass", "protected-mode",
                "hz", "tcp-keepalive"
            };
            return commonKeys
                .Where(k => k.StartsWith(currentToken, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        return Array.Empty<string>();
    }

    private static string[] SuggestKeysPatterns(string currentToken)
    {
        var patterns = new[] { "*", "user:*", "session:*", "counter*", "temp*" };
        return patterns
            .Where(p => p.StartsWith(currentToken, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static string[] SuggestKeys(string currentToken)
    {
        // Static known keys + dynamic from history (simple version)
        var known = GetKnownKeys()
            .Where(k => k.StartsWith(currentToken, StringComparison.OrdinalIgnoreCase));

        // Optional: collect from recent successful KEYS commands (future improvement)
        return known.ToArray();
    }

    private static string[] SuggestKeysOrFields(string command, string currentToken, IReadOnlyList<string> tokens)
    {
        // If first argument (key) → suggest keys
        if (tokens.Count == 1 || (tokens.Count == 2 && !tokens[1].EndsWith(" ")))
        {
            return SuggestKeys(currentToken);
        }

        // For hashes (field suggestions) – static for now
        if (command.StartsWith("H"))
        {
            var fields = new[] { "name", "age", "email", "status", "score" };
            return fields
                .Where(f => f.StartsWith(currentToken, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        return Array.Empty<string>();
    }

    private static string[] SuggestCounters(string currentToken)
    {
        // Counters usually have names like "visits", "views", "likes", "counter"
        var counters = new[] { "counter", "visits", "views", "likes", "hits", "users:online" };
        return counters
            .Where(c => c.StartsWith(currentToken, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    // ────────────────────────────────────────────────
    // Static helpers (expand as needed)
    // ────────────────────────────────────────────────
    private static IEnumerable<string> GetKnownKeys()
    {
        // Static examples — expand this list over time
        yield return "mykey";
        yield return "hello";
        yield return "counter";
        yield return "user:123";
        yield return "session:abc123";
        yield return "visits";
        yield return "views";
        yield return "likes";
        yield return "temp";
        yield return "config:test";
        yield return "cache:home";
    }
}