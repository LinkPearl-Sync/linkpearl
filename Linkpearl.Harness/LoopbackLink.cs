using System.Threading.Channels;
using LiteNetLib;

namespace Linkpearl.Harness;

/// <summary>Une trame reçue, avec son canal.</summary>
public sealed record Incoming(byte Channel, byte[] Payload);

/// <summary>
/// Deux NetManager reliés en boucle locale, avec des files d'attente asynchrones.
/// </summary>
/// <remarks>
/// C'est bien LiteNetLib qui transporte, et non un raccourci en mémoire : les
/// limites de fenêtre, de fragmentation et de file d'envoi sont donc celles que
/// le plugin rencontrera.
/// </remarks>
public sealed class LoopbackLink : IAsyncDisposable
{
    private readonly NetManager _server;
    private readonly NetManager _client;
    private readonly Channel<Incoming> _toServer = Channel.CreateUnbounded<Incoming>();
    private readonly Channel<Incoming> _toClient = Channel.CreateUnbounded<Incoming>();
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Thread> _pumps = [];

    private NetPeer? _clientPeer;
    private NetPeer? _serverPeer;

    public LoopbackLink(int channels)
    {
        var serverListener = new EventBasedNetListener();
        _server = new NetManager(serverListener)
        {
            ChannelsCount = (byte)channels, AutoRecycle = true, DisconnectTimeout = 120_000,
        };

        serverListener.ConnectionRequestEvent += request => _serverPeer = request.AcceptIfKey("linkpearl-e2e");
        serverListener.NetworkReceiveEvent += (_, reader, channel, _) =>
        {
            _toServer.Writer.TryWrite(new Incoming(channel, reader.GetRemainingBytes()));
            reader.Recycle();
        };

        var clientListener = new EventBasedNetListener();
        _client = new NetManager(clientListener)
        {
            ChannelsCount = (byte)channels, AutoRecycle = true, DisconnectTimeout = 120_000,
        };

        clientListener.NetworkReceiveEvent += (_, reader, channel, _) =>
        {
            _toClient.Writer.TryWrite(new Incoming(channel, reader.GetRemainingBytes()));
            reader.Recycle();
        };

        _server.Start();
        _client.Start();

        Pump(_server);
        Pump(_client);
    }

    public int ServerPort => _server.LocalPort;

    public float PacketLossPercent => _server.Statistics.PacketLossPercent;

    public int RoundTripMs => _clientPeer?.Ping ?? 0;

    public void Connect(int port)
    {
        _clientPeer = _client.Connect("127.0.0.1", port, "linkpearl-e2e");

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (_clientPeer.ConnectionState is not ConnectionState.Connected)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("connexion en boucle locale impossible");

            Thread.Sleep(5);
        }
    }

    public void SendFromClient(byte channel, byte[] payload)
    {
        // Contre-pression : la file d'envoi de LiteNetLib n'est pas bornée, donc
        // sans cette attente on allouerait tout le transfert d'avance.
        while (_clientPeer!.GetPacketsCountInReliableQueue(channel, true) > 128)
            Thread.Sleep(1);

        _clientPeer.Send(payload, channel, DeliveryMethod.ReliableOrdered);
    }

    public void SendFromServer(byte channel, byte[] payload)
        => _serverPeer!.Send(payload, channel, DeliveryMethod.ReliableOrdered);

    public ValueTask<Incoming> ReceiveOnServerAsync(CancellationToken ct) => _toServer.Reader.ReadAsync(ct);

    public ValueTask<Incoming> ReceiveOnClientAsync(CancellationToken ct) => _toClient.Reader.ReadAsync(ct);

    private void Pump(NetManager manager)
    {
        var thread = new Thread(() =>
        {
            while (_stop.IsCancellationRequested is false)
            {
                manager.PollEvents();
                Thread.Sleep(1);
            }
        })
        { IsBackground = true };

        thread.Start();
        _pumps.Add(thread);
    }

    public ValueTask DisposeAsync()
    {
        _stop.Cancel();

        foreach (var pump in _pumps)
            pump.Join(2000);

        _client.Stop();
        _server.Stop();
        _stop.Dispose();

        return ValueTask.CompletedTask;
    }
}
