using System.Net.Sockets;

namespace DevCache;

public sealed class CommandContext
{
    public required TcpClient Client { get; init; }
    public required NetworkStream Stream { get; init; }
    public required RespWriter Writer { get; init; }
}
