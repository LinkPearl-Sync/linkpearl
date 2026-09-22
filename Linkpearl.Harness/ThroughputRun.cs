using System.Diagnostics;
using System.Net;
using LiteNetLib;

namespace Linkpearl.Harness;

public sealed record RunSettings(
    long TotalBytes, int Channels, int BlockSize,
    int LatencyMs, int JitterMs, double LossPercent, int PollMs, int Seed);

public sealed record RunResult(
    RunSettings Settings, double Seconds, double MegabytesPerSecond,
    int ReportedPing, float ReportedLossPercent, long PeakWorkingSetDeltaBytes);

/// <summary>
/// Mesure le débit applicatif de LiteNetLib entre deux NetManager, à travers un
/// relais qui applique retard et perte.
/// </summary>
/// <remarks>
/// Le but n'est pas de faire marcher un transfert mais de trouver le plafond.
/// La fenêtre fiable de LiteNetLib est une constante de 64 paquets par canal,
/// donc environ 91 Kio en vol : ce banc doit montrer ce que cela donne selon le
/// nombre de canaux, et si le multi-canal suffit à compenser.
/// </remarks>
public static class ThroughputRun
{
    private const byte ControlChannel = 0;

    public static RunResult Execute(RunSettings settings)
    {
        var received = 0L;
        var done = new ManualResetEventSlim(false);

        var serverListener = new EventBasedNetListener();
        var server = new NetManager(serverListener)
        {
            ChannelsCount = (byte)settings.Channels,
            AutoRecycle = true,
            DisconnectTimeout = 60_000,
        };

        serverListener.ConnectionRequestEvent += request => request.AcceptIfKey("linkpearl-bench");
        serverListener.NetworkReceiveEvent += (_, reader, _, _) =>
        {
            var total = Interlocked.Add(ref received, reader.AvailableBytes);
            reader.Recycle();

            if (total >= settings.TotalBytes)
                done.Set();
        };

        server.Start();

        using var proxy = new ImpairmentProxy(
            new IPEndPoint(IPAddress.Loopback, server.LocalPort),
            settings.LatencyMs, settings.JitterMs, settings.LossPercent, settings.Seed);

        var clientListener = new EventBasedNetListener();
        var client = new NetManager(clientListener)
        {
            ChannelsCount = (byte)settings.Channels,
            AutoRecycle = true,
            DisconnectTimeout = 60_000,
        };
        client.Start();

        var peer = client.Connect("127.0.0.1", proxy.Port, "linkpearl-bench");

        using var pumping = new CancellationTokenSource();
        var pumps = new[]
        {
            Pump(server, settings.PollMs, pumping.Token),
            Pump(client, settings.PollMs, pumping.Token),
        };

        WaitForConnection(peer);

        var process = Process.GetCurrentProcess();
        var workingSetBefore = process.WorkingSet64;

        var block = new byte[settings.BlockSize];
        Random.Shared.NextBytes(block);

        var watch = Stopwatch.StartNew();
        var sent = 0L;
        var channel = (byte)1;

        while (sent < settings.TotalBytes)
        {
            // La file d'envoi de LiteNetLib n'est pas bornée : sans cette
            // attente, on allouerait la totalité du transfert en paquets avant
            // que le premier ne parte.
            while (peer.GetPacketsCountInReliableQueue(channel, true) > 128)
                Thread.Sleep(1);

            peer.Send(block, channel, DeliveryMethod.ReliableOrdered);
            sent += block.Length;

            channel = (byte)(channel % (settings.Channels - 1) + 1);
        }

        done.Wait(TimeSpan.FromMinutes(20));
        watch.Stop();

        process.Refresh();
        var workingSetAfter = process.WorkingSet64;
        var ping = peer.Ping;
        var loss = server.Statistics.PacketLossPercent;

        pumping.Cancel();
        foreach (var pump in pumps)
            pump.Join(2000);

        client.Stop();
        server.Stop();

        var seconds = watch.Elapsed.TotalSeconds;

        return new RunResult(
            settings, seconds,
            Interlocked.Read(ref received) / 1024.0 / 1024.0 / seconds,
            ping, loss,
            workingSetAfter - workingSetBefore);
    }

    /// <summary>
    /// Boucle de PollEvents, à l'intervalle demandé.
    /// </summary>
    /// <remarks>
    /// Dans le jeu, PollEvents partira de Framework.Update, donc à la fréquence
    /// d'affichage. L'intervalle est un paramètre de la mesure et non une
    /// constante : c'est peut-être lui qui plafonne, et non la fenêtre.
    /// </remarks>
    private static Thread Pump(NetManager manager, int pollMs, CancellationToken ct)
    {
        var thread = new Thread(() =>
        {
            while (ct.IsCancellationRequested is false)
            {
                manager.PollEvents();
                Thread.Sleep(pollMs);
            }
        })
        { IsBackground = true };

        thread.Start();
        return thread;
    }

    private static void WaitForConnection(NetPeer peer)
    {
        var deadline = Stopwatch.StartNew();

        while (peer.ConnectionState is not ConnectionState.Connected)
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(30))
                throw new TimeoutException("connexion au banc impossible");

            Thread.Sleep(10);
        }
    }
}
