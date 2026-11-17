using System.Net;
using System.Net.Sockets;

namespace FrubberPOS.Tests.Infrastructure.Workstations;

/// <summary>Forwards TLS unchanged, and can withhold server bytes at a test barrier.</summary>
internal sealed class RecoveryLoopbackRelay : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly Task run;
    private int holdReplies;

    public RecoveryLoopbackRelay(int destinationPort)
    {
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        run = RunAsync(destinationPort);
    }

    public int Port { get; }

    public void HoldReplies() => Interlocked.Exchange(ref holdReplies, 1);

    public void Cut() => stop.Cancel();

    private async Task RunAsync(int destinationPort)
    {
        var connections = new List<Task>();
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stop.Token);
                connections.Add(ForwardAsync(client, destinationPort));
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        finally
        {
            listener.Stop();
            await Task.WhenAll(connections);
        }
    }

    private async Task ForwardAsync(TcpClient client, int destinationPort)
    {
        using var local = client;
        using var remote = new TcpClient();
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        try
        {
            await remote.ConnectAsync(IPAddress.Loopback, destinationPort, connection.Token);
            var toServer = local.GetStream().CopyToAsync(remote.GetStream(), connection.Token);
            var toClient = ForwardRepliesAsync(remote.GetStream(), local.GetStream(), connection.Token);
            await Task.WhenAny(toServer, toClient);
            await connection.CancelAsync();
            await Task.WhenAll(toServer, toClient);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException)
        {
        }
    }

    private async Task ForwardRepliesAsync(NetworkStream source, NetworkStream destination, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        while (await source.ReadAsync(buffer, cancellationToken) is var read && read != 0)
        {
            if (Volatile.Read(ref holdReplies) == 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Cut();
        await run;
        stop.Dispose();
    }
}
