using DevCache.Core.Models;
using DevCache.Core.Storage;
using System.Text;

namespace DevCache.Core.Commands;

public class InfoCommandHandler
{
    private readonly InMemoryStore _store;
    private readonly ServerRuntimeInfo _runtime;

    public InfoCommandHandler(InMemoryStore store, ServerRuntimeInfo runtime)
    {
        _store = store;
        _runtime = runtime;
    }

    public string Execute(string[] args)
    {
        string section = args.Length > 1 ? args[1].ToLowerInvariant() : "all";

        var sb = new StringBuilder();

        if (section is "all" or "server")
            AppendServer(sb);

        if (section is "all" or "memory")
            AppendMemory(sb);

        if (section is "all" or "keyspace")
            AppendKeyspace(sb);

        if (section is "all" or "stats")
            AppendStats(sb);

        // Future: clients, persistence, replication, cpu, commandstats, ...

        return sb.Length > 0 ? sb.ToString().TrimEnd() : "# No matching section";
    }

    private void AppendServer(StringBuilder sb)
    {
        var uptime = DateTime.UtcNow - _runtime.StartedAt;

        sb.AppendLine("# Server");
        sb.AppendLine("devcache_version:0.3.0");           // change when you tag releases
        sb.AppendLine("devcache_mode:standalone");
        sb.AppendLine($"process_id:{Environment.ProcessId}");
        sb.AppendLine($"tcp_port:{_runtime.Port}");
        sb.AppendLine($"uptime_in_seconds:{(long)uptime.TotalSeconds}");
        sb.AppendLine($"uptime_in_days:{(int)uptime.TotalDays}");
        sb.AppendLine($"executable:{Environment.ProcessPath ?? "unknown"}");
        if (_runtime.ConfigFile is not null)
            sb.AppendLine($"config_file:{_runtime.ConfigFile}");
        sb.AppendLine();
    }

    private void AppendMemory(StringBuilder sb)
    {
        long used = _store.GetApproximateMemoryBytesUsed();
        long max = _runtime.MaxMemoryBytes;

        sb.AppendLine("# Memory");
        sb.AppendLine($"used_memory:{used}");
        sb.AppendLine($"used_memory_human:{FormatBytes(used)}");
        sb.AppendLine("used_memory_peak:0");               // add peak tracking later
        sb.AppendLine("used_memory_peak_human:0B");
        sb.AppendLine($"maxmemory:{max}");
        sb.AppendLine($"maxmemory_human:{FormatBytes(max)}");
        sb.AppendLine("maxmemory_policy:noeviction");      // hardcoded for now
        sb.AppendLine("mem_fragmentation_ratio:1.00");     // placeholder
        sb.AppendLine("mem_allocator:system");             // can detect later
        sb.AppendLine();
    }

    private void AppendKeyspace(StringBuilder sb)
    {
        var stats = _store.GetDbStatistics(0);

        sb.AppendLine("# Keyspace");
        sb.Append($"db0:keys={stats.KeyCount},expires={stats.KeysWithExpiry}");

        if (stats.AverageTtlMs is { } avg && avg > 0)
            sb.Append($",avg_ttl={(long)Math.Round(avg)}");

        sb.AppendLine();
        sb.AppendLine();
    }

    private void AppendStats(StringBuilder sb)
    {
        sb.AppendLine("# Stats");
        sb.AppendLine($"total_commands_processed:{_store.TotalCommandsProcessed}");
        sb.AppendLine("instantaneous_ops_per_sec:0");      // can compute later with sliding window
        sb.AppendLine($"keyspace_hits:{_store.KeyspaceHits}");
        sb.AppendLine($"keyspace_misses:{_store.KeyspaceMisses}");
        sb.AppendLine($"expired_keys:{_store.ExpiredKeys}");
        sb.AppendLine($"evicted_keys:{_store.EvictedKeys}");
        sb.AppendLine();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes == 0) return "0B";
        string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
        int i = 0;
        double val = bytes;
        while (val >= 1024 && i < units.Length - 1)
        {
            val /= 1024;
            i++;
        }

        return val switch
        {
            < 10 => $"{val:F2}{units[i]}".Replace(".00", "").Replace(".0", ""),
            < 100 => $"{val:F1}{units[i]}".Replace(".00", "").Replace(".0", ""),
            _ => $"{(long)Math.Round(val)}{units[i]}".Replace(".00", "").Replace(".0", "")
        };
    }
}