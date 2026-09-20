using System.Net;
using System.Net.Sockets;

namespace Odonomics.Walk;

public static class CdpAvailability
{
    public static async Task<bool> IsListeningAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(IPAddress.Loopback, port, timeoutCts.Token);
            return client.Connected;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return false;
        }
    }
}
