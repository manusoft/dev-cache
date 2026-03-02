using ManuHub.Memora.Models;
using ManuHub.Memora.Storage;
using System.Text;

namespace ManuHub.Memora.Commands.Core;

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
        string section = args.Length > 0 ? args[0].ToLowerInvariant() : "all";

        var sb = new StringBuilder();

        if (section is "all" or "server") AppendServer(sb);
        if (section is "all" or "memory") AppendMemory(sb);
        if (section is "all" or "keyspace") AppendKeyspace(sb);
        if (section is "all" or "stats") AppendStats(sb);
        if (section is "all" or "replication") AppendReplication(sb);
        if (section is "all" or "clients") AppendClients(sb);
        if (section is "all" or "persistence") AppendPersistence(sb);

        // Future: cpu, commandstats, ...

        return sb.Length > 0 ? sb.ToString().TrimEnd() : "# No matching section";
    }

    private void AppendServer(StringBuilder sb)
    {
        var uptime = DateTime.UtcNow - _runtime.StartedAt;

        sb.AppendLine("# Server");
        sb.AppendLine("memora_version:0.3.0");           // change when you tag releases
        sb.AppendLine("memora_mode:standalone");
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
        sb.AppendLine($"used_memory_peak:{used}"); // simple peak for now
        sb.AppendLine($"used_memory_peak_human:{FormatBytes(used)}");

        sb.AppendLine($"maxmemory:{max}");
        sb.AppendLine($"maxmemory_human:{FormatBytes(max)}");
        sb.AppendLine($"maxmemory_policy:{_runtime.MaxMemoryPolicy}");

        sb.AppendLine("mem_fragmentation_ratio:1.00");     // placeholder
        sb.AppendLine("mem_allocator:system");             // can detect later

        sb.AppendLine($"evicted_keys:{_store.EvictedKeys}");

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
        sb.AppendLine($"instantaneous_ops_per_sec:{_store.InstantaneousOpsPerSec}");
        sb.AppendLine($"keyspace_hits:{_store.KeyspaceHits}");
        sb.AppendLine($"keyspace_misses:{_store.KeyspaceMisses}");
        sb.AppendLine($"expired_keys:{_store.ExpiredKeys}");
        sb.AppendLine($"evicted_keys:{_store.EvictedKeys}");
        sb.AppendLine();
    }

    private void AppendReplication(StringBuilder sb)
    {
        sb.AppendLine("# Replication");
        sb.AppendLine("role:master");                      // we are always master for now
        sb.AppendLine("connected_slaves:0");               // no real replicas yet
        sb.AppendLine("master_repl_offset:0");             // no writes tracked yet
        sb.AppendLine("repl_backlog_active:0");
        sb.AppendLine("repl_backlog_size:1048576");        // typical default 1 MB
        sb.AppendLine("repl_backlog_first_byte_offset:0");
        sb.AppendLine("repl_backlog_histlen:0");
        sb.AppendLine();
    }

    private void AppendClients(StringBuilder sb)
    {
        sb.AppendLine("# Clients");
        sb.AppendLine($"connected_clients:1");     // placeholder – later count real connections
        sb.AppendLine("maxclients:10000");         // or from config
        sb.AppendLine();
    }

    private void AppendPersistence(StringBuilder sb)
    {
        sb.AppendLine("# Persistence");

        // AOF is always enabled in your current design
        sb.AppendLine("aof_enabled:yes");
        sb.AppendLine("rdb_enabled:no");                    // if you never add RDB

        long aofSize = _store.AofFileSizeBytes;
        bool rewriteInProgress = false;                     // you can set this flag during rewrite

        sb.AppendLine($"aof_current_size:{aofSize}");
        sb.AppendLine($"aof_current_size_human:{FormatBytes(aofSize)}");
        sb.AppendLine($"aof_rewrite_in_progress:{(rewriteInProgress ? "yes" : "no")}");
        sb.AppendLine("aof_rewrite_scheduled:no");
        sb.AppendLine("aof_last_rewrite_time_sec:0");       // can track real value later
        sb.AppendLine("aof_last_bgrewrite_status:ok");      // or "err" if failed
        sb.AppendLine("aof_last_write_status:ok");
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