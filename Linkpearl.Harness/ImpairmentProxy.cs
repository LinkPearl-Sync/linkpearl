using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Linkpearl.Harness;

/// <summary>
/// Relais UDP qui applique retard, gigue et perte entre deux extrémités.
/// </summary>
/// <remarks>
/// Mesurer le plafond de fenêtre de LiteNetLib demande une latence réelle entre
/// les deux pairs. <c>tc netem</c> exigerait les privilèges root ; un relais en
/// espace utilisateur n'en demande aucun, et il est en prime déterministe à
/// graine fixée, donc une mesure se rejoue à l'identique.
///
/// La file d'attente est ordonnée par instant de libération et drainée par un
/// fil dédié à la milliseconde : <c>Task.Delay</c> aurait une granularité trop
/// grossière et fausserait les mesures aux faibles latences.
/// </remarks>
public sealed class ImpairmentProxy : IDisposable
{
    private readonly Socket _socket;
    private readonly IPEndPoint _target;
    private readonly int _latencyMs;
    private readonly int _jitterMs;
    private readonly double _lossRatio;
    private readonly Random _random;
    private readonly PriorityQueue<(byte[] Data, EndPoint To), long> _pending = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource _stop = new();

    private EndPoint? _client;

    public int Port { get; }

    public long Dropped { get; private set; }
    public long Forwarded { get; private set; }

    public ImpairmentProxy(IPEndPoint target, int latencyMs, int jitterMs, double lossPercent, int seed)
    {
        _target    = target;
        _latencyMs = latencyMs;
        _jitterMs  = jitterMs;
        _lossRatio = lossPercent / 100.0;
        _random    = new Random(seed);

        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        Port = ((IPEndPoint)_socket.LocalEndPoint!).Port;

        new Thread(ReceiveLoop) { IsBackground = true, Name = "proxy-rx" }.Start();
        new Thread(DrainLoop)   { IsBackground = true, Name = "proxy-tx" }.Start();
    }

    private void ReceiveLoop()
    {
        var buffer = new byte[2048];

        while (_stop.IsCancellationRequested is false)
        {
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            int read;

            try
            {
                read = _socket.ReceiveFrom(buffer, ref from);
            }
            catch (SocketException)
            {
                continue;   // datagramme refusé : sans conséquence sur la mesure
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            // Le premier venu qui n'est pas la cible est le client.
            var toTarget = from.Equals(_target) is false;
            if (toTarget)
                _client = from;

            var destination = toTarget ? _target : _client;
            if (destination is null)
                continue;

            lock (_pending)
            {
                if (_random.NextDouble() < _lossRatio)
                {
                    Dropped++;
                    continue;
                }

                var jitter = _jitterMs == 0 ? 0 : _random.Next(-_jitterMs, _jitterMs + 1);
                var release = _clock.ElapsedMilliseconds + Math.Max(0, _latencyMs + jitter);

                _pending.Enqueue((buffer[..read].ToArray(), destination), release);
            }
        }
    }

    private void DrainLoop()
    {
        while (_stop.IsCancellationRequested is false)
        {
            var now = _clock.ElapsedMilliseconds;

            while (true)
            {
                (byte[] Data, EndPoint To) item;

                lock (_pending)
                {
                    if (_pending.TryPeek(out _, out var release) is false || release > now)
                        break;

                    item = _pending.Dequeue();
                    Forwarded++;
                }

                try
                {
                    _socket.SendTo(item.Data, item.To);
                }
                catch (SocketException)
                {
                    // idem : un envoi refusé ne doit pas arrêter la mesure
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }

            Thread.Sleep(1);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _socket.Dispose();
        _stop.Dispose();
    }
}
