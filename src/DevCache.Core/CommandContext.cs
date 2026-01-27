using DevCache.Common;
using System.Net.Sockets;

namespace DevCache.Core;

public sealed class CommandContext
{
    public required TcpClient Client { get; init; }
    public required NetworkStream Stream { get; init; }
    public required RespWriter Writer { get; init; }   
    public DateTimeOffset ConnectedAt { get; init; } = DateTimeOffset.UtcNow;
    public bool IsAuthenticated { get; set; } = false;
}