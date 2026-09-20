using System.Net;
using System.Net.Sockets;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

public class CdpAvailabilityTests
{
    [Fact]
    public async Task IsListeningAsync_NothingBoundToPort_ReturnsFalse()
    {
        // Port 1 is a reserved low port nothing binds to in a test sandbox.
        bool listening = await CdpAvailability.IsListeningAsync(1, CancellationToken.None);

        Assert.False(listening);
    }

    [Fact]
    public async Task IsListeningAsync_SomethingBoundToPort_ReturnsTrue()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        bool listening = await CdpAvailability.IsListeningAsync(port, CancellationToken.None);

        Assert.True(listening);
        listener.Stop();
    }
}
