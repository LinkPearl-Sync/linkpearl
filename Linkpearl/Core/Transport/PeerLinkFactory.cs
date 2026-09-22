using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Transport;

/// <summary>
/// Ouvre et accepte les liens avec les pairs, sur une seule socket.
/// </summary>
/// <remarks>
/// Une seule socket pour tous les pairs, et c'est nécessaire : c'est son
/// adresse publique que le rendez-vous nous a rendue, et c'est donc elle qui a
/// percé le NAT. Ouvrir une socket par pair rendrait cette adresse inutile.
///
/// Le jeton de connexion est dérivé du secret de paire : un inconnu ne peut pas
/// ouvrir de session, même en connaissant l'adresse.
/// </remarks>
public sealed class PeerLinkFactory : IDisposable
{
    private readonly NetManager _manager;
    private readonly EventBasedNetListener _listener = new();
    private readonly ILogSink _log;

    private readonly ConcurrentDictionary<NetPeer, LiteNetPeerLink> _links = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IPeerLink>> _pending =
        new(StringComparer.Ordinal);

    /// <summary>Les jetons acceptés, par pair. Tout le reste est refusé.</summary>
    private readonly ConcurrentDictionary<string, string> _acceptedTokens = new(StringComparer.Ordinal);

    public PeerLinkFactory(int channels, ILogSink log)
    {
        _log = log;

        _manager = new NetManager(_listener)
        {
            ChannelsCount = (byte)channels,
            AutoRecycle = true,
            DisconnectTimeout = 20_000,
            UnsyncedEvents = false,
        };

        _listener.ConnectionRequestEvent += OnConnectionRequest;
        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.NetworkReceiveEvent += OnReceive;
        _listener.NetworkReceiveUnconnectedEvent += OnUnconnected;

        _manager.UnconnectedMessagesEnabled = true;
        _manager.Start();
    }

    public int LocalPort => _manager.LocalPort;

    /// <summary>Un pair a ouvert une session vers nous.</summary>
    public event Action<string, IPeerLink>? Accepted;

    /// <summary>Autorise un jeton, et donc le pair qui le présentera.</summary>
    public void Allow(string token, string peerLabel) => _acceptedTokens[token] = peerLabel;

    public void Forget(string token) => _acceptedTokens.TryRemove(token, out _);

    /// <summary>
    /// Tente de joindre un pair sur chacune de ses adresses candidates.
    /// </summary>
    /// <remarks>
    /// Toutes en parallèle et par rafales répétées : le NatPunchModule de
    /// LiteNetLib n'envoie que deux paquets sans réessai, ce qui échoue dès que
    /// les deux côtés ne sont pas synchronisés à quelques dizaines de
    /// millisecondes près.
    /// </remarks>
    public async Task<IPeerLink?> ConnectAsync(
        IReadOnlyList<IPEndPoint> candidates, string token, TimeSpan budget, CancellationToken ct)
    {
        if (candidates.Count == 0)
            return null;

        var completion = new TaskCompletionSource<IPeerLink>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[token] = completion;
        _acceptedTokens[token] = "sortant";

        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(budget);

            var attempts = Task.Run(async () =>
            {
                while (deadline.IsCancellationRequested is false)
                {
                    foreach (var candidate in candidates)
                    {
                        try
                        {
                            _manager.Connect(candidate, token);
                        }
                        catch (Exception e)
                        {
                            _log.Debug($"Tentative vers {candidate} refusée : {e.Message}");
                        }
                    }

                    await Task.Delay(500, deadline.Token).ConfigureAwait(false);
                }
            }, deadline.Token);

            var finished = await Task.WhenAny(completion.Task, Task.Delay(budget, ct)).ConfigureAwait(false);

            return finished == completion.Task ? await completion.Task.ConfigureAwait(false) : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _pending.TryRemove(token, out _);
        }
    }

    /// <summary>À appeler depuis le thread du jeu, à chaque image.</summary>
    public void Poll() => _manager.PollEvents();

    /// <summary>
    /// Demande au rendez-vous l'adresse publique de <em>cette</em> socket.
    /// </summary>
    /// <remarks>
    /// Par un message hors connexion, donc sur la socket même qui portera les
    /// liens. C'est essentiel : le NAT associe une adresse publique à une socket
    /// précise, et découvrir celle d'une autre socket donnerait une adresse
    /// que le pair ne pourrait pas joindre.
    /// </remarks>
    public async Task<IPEndPoint?> ReflectAsync(IPEndPoint rendezvous, TimeSpan timeout, CancellationToken ct)
    {
        var completion = new TaskCompletionSource<IPEndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
        _reflection = completion;

        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(timeout);

            var writer = new NetDataWriter();
            writer.Put(RendezvousKind.Reflect);

            while (deadline.IsCancellationRequested is false && completion.Task.IsCompleted is false)
            {
                _manager.SendUnconnectedMessage(writer, rendezvous);
                await Task.Delay(300, deadline.Token).ConfigureAwait(false);
            }

            return completion.Task.IsCompletedSuccessfully ? await completion.Task.ConfigureAwait(false) : null;
        }
        catch (OperationCanceledException)
        {
            return completion.Task.IsCompletedSuccessfully ? await completion.Task.ConfigureAwait(false) : null;
        }
        finally
        {
            _reflection = null;
        }
    }

    private TaskCompletionSource<IPEndPoint>? _reflection;

    private void OnUnconnected(IPEndPoint from, NetPacketReader reader, UnconnectedMessageType type)
    {
        var data = reader.GetRemainingBytes();
        reader.Recycle();

        if (data.Length < 4 || data[0] != RendezvousKind.Reflected)
            return;

        var length = data[1];

        if (data.Length < 2 + length + 2)
            return;

        try
        {
            var address = new IPAddress(data.AsSpan(2, length));
            var port = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2 + length));
            _reflection?.TrySetResult(new IPEndPoint(address, port));
        }
        catch (Exception e)
        {
            _log.Debug($"Réflexion illisible : {e.Message}");
        }
    }

    private void OnConnectionRequest(ConnectionRequest request)
    {
        var token = request.Data.GetString(128);

        if (_acceptedTokens.ContainsKey(token) is false)
        {
            // Un inconnu, ou un jeton périmé : on refuse sans rien révéler.
            request.Reject();
            return;
        }

        request.Accept();
    }

    private void OnPeerConnected(NetPeer peer)
    {
        var link = new LiteNetPeerLink(peer);
        _links[peer] = link;

        // Le jeton n'est pas rendu par LiteNetLib côté sortant : on résout la
        // promesse en attente s'il n'y en a qu'une, sinon on traite le lien
        // comme entrant et la couche supérieure l'identifiera au handshake.
        var pending = _pending.FirstOrDefault(p => p.Value.Task.IsCompleted is false);

        if (pending.Value is not null && pending.Value.TrySetResult(link))
            return;

        Accepted?.Invoke("entrant", link);
    }

    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
    {
        if (_links.TryRemove(peer, out var link))
            link.Close(info.Reason.ToString());
    }

    private void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
    {
        if (_links.TryGetValue(peer, out var link))
            link.Deliver(channel, reader.GetRemainingBytes());

        reader.Recycle();
    }

    /// <summary>Nos adresses locales, à offrir comme candidats au pair.</summary>
    /// <remarks>
    /// Utiles quand les deux joueurs sont sous le même toit : le NAT ne
    /// laisserait pas forcément revenir un paquet parti vers sa propre adresse
    /// publique.
    /// </remarks>
    public IReadOnlyList<IPEndPoint> LocalCandidates()
        => System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus is System.Net.NetworkInformation.OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily is AddressFamily.InterNetwork && IPAddress.IsLoopback(a) is false)
            .Select(a => new IPEndPoint(a, LocalPort))
            .ToList();

    public void Dispose()
    {
        _manager.Stop();
    }
}
