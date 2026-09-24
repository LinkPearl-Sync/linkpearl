using System.Security.Cryptography;
using System.Threading.Channels;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Groups;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Protocol;
using Linkpearl.Core.Transport;

namespace Linkpearl.Core.Sync;

/// <summary>Où en est une session avec un pair.</summary>
public enum PeerSessionState
{
    Disconnected,
    Connecting,
    Handshaking,

    /// <summary>Lien établi et authentifié, mais le pair n'est pas dans notre champ.</summary>
    Connected,

    /// <summary>Le pair est visible et son apparence a été posée.</summary>
    Applied,
}

/// <summary>Une trame applicative reçue d'un pair, déjà déchiffrée.</summary>
public sealed record PeerMessage(byte Kind, byte Channel, byte[] Payload);

/// <summary>
/// Une session authentifiée avec un pair.
/// </summary>
/// <remarks>
/// Qui initie le handshake se décide par comparaison des identifiants, et non
/// par « celui qui a appelé » : les deux côtés tentent de se joindre en même
/// temps, et sans règle déterministe on obtiendrait deux initiateurs ou deux
/// répondeurs.
/// </remarks>
public sealed class PeerSession : IAsyncDisposable
{
    private readonly IPeerLink _link;
    private readonly ILogSink _log;
    private readonly Channel<PeerMessage> _incoming = Channel.CreateUnbounded<PeerMessage>();
    private readonly Channel<byte[]> _rawIncoming = Channel.CreateUnbounded<byte[]>();

    /// <summary>
    /// Tient ensemble le passage au mode scellé et le rangement d'une trame reçue.
    /// </summary>
    private readonly Lock _sealing = new();

    private SecureChannel? _secure;

    /// <summary>Ce qui admet la clé d'un pair de groupe. Null quand le moteur n'a pas de groupes.</summary>
    private IGroupGate? _groups;

    private PeerSession(IPeerLink link, PairRecord pair, ILogSink log)
    {
        _link = link;
        Pair = pair;
        _log = log;

        link.Received += OnReceived;
        link.Closed += reason => State = PeerSessionState.Disconnected;
    }

    public PairRecord Pair { get; }

    /// <summary>Le lien qui porte la session, pour qui doit en observer la santé.</summary>
    public IPeerLink Link => _link;

    public PeerSessionState State { get; private set; } = PeerSessionState.Connecting;

    public byte[]? SessionId { get; private set; }

    public ChannelReader<PeerMessage> Messages => _incoming.Reader;

    /// <summary>
    /// Établit une session : handshake puis canal chiffré.
    /// </summary>
    /// <remarks>
    /// L'autorisation vient du carnet local et de lui seul. La clé publique
    /// reçue doit correspondre à l'empreinte du pair attendu, faute de quoi la
    /// session est refusée : c'est ce qui rend un rendez-vous malveillant
    /// incapable d'imposer quelqu'un d'autre.
    /// </remarks>
    public static async Task<PeerSession?> EstablishAsync(
        IPeerLink link, PairRecord pair, PeerId ourId, ECDsa identity, IClock clock, ILogSink log,
        CancellationToken ct, IGroupGate? groups = null)
    {
        var session = new PeerSession(link, pair, log) { State = PeerSessionState.Handshaking, _groups = groups };

        try
        {
            var weInitiate = WeInitiate(pair, ourId);

            var keys = weInitiate
                ? await session.InitiateAsync(identity, pair, clock, ct).ConfigureAwait(false)
                : await session.RespondAsync(identity, pair, clock, ct).ConfigureAwait(false);

            if (keys is null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
                return null;
            }

            session.Seal(new SecureChannel(keys.SendKey, keys.ReceiveKey, keys.SessionId));
            session.SessionId = keys.SessionId;
            session.State = PeerSessionState.Connected;

            _ = Task.Run(() => session.PumpAsync(ct), ct);

            log.Info($"{pair.DisplayName} : session établie, {(weInitiate ? "initiateur" : "répondeur")}.");
            return session;
        }
        catch (Exception e)
        {
            log.Warning($"{pair.DisplayName} : handshake en échec.", e);
            await session.DisposeAsync().ConfigureAwait(false);
            return null;
        }
    }

    public async ValueTask SendAsync(byte channel, byte kind, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (_secure is null)
            throw new InvalidOperationException("session non établie");

        await _link.SendAsync(channel, _secure.Seal(channel, kind, payload.Span), ct).ConfigureAwait(false);
    }

    public void MarkApplied() => State = PeerSessionState.Applied;

    public void MarkOutOfSight()
    {
        // On revient à « connecté » sans couper la session ni jeter le cache :
        // le pair va revenir, et tout refaire coûterait un transfert complet.
        if (State is PeerSessionState.Applied)
            State = PeerSessionState.Connected;
    }

    /// <summary>
    /// Qui des deux envoie le premier message.
    /// </summary>
    /// <remarks>
    /// Pour un pair de groupe, l'identifiant du pair est tiré du secret du
    /// couple et vaut la même chose des deux côtés : le comparer au nôtre
    /// pourrait donner deux initiateurs. Les deux empreintes, elles, sont
    /// connues des deux et différentes.
    /// </remarks>
    private static bool WeInitiate(PairRecord pair, PeerId ourId)
        => pair.Group is { } origin
            ? string.CompareOrdinal(origin.Ours.ToString(), origin.Theirs.ToString()) < 0
            : string.CompareOrdinal(ourId.ToHex(), pair.Id.ToHex()) < 0;

    private async Task<SessionKeys?> InitiateAsync(ECDsa identity, PairRecord pair, IClock clock, CancellationToken ct)
    {
        var initiator = new HandshakeInitiator(identity, clock);

        await _link.SendAsync(0, initiator.CreateMessage1(), ct).ConfigureAwait(false);

        var message2 = await NextRawAsync(ct).ConfigureAwait(false);

        if (initiator.TryHandleMessage2(message2, key => Authorizes(pair, key, _groups), out var message3, out var keys, out var why) is false)
        {
            _log.Warning($"{pair.DisplayName} : message 2 refusé, {why}");
            return null;
        }

        await _link.SendAsync(0, message3!, ct).ConfigureAwait(false);
        return keys;
    }

    private async Task<SessionKeys?> RespondAsync(ECDsa identity, PairRecord pair, IClock clock, CancellationToken ct)
    {
        var responder = new HandshakeResponder(identity, clock);

        var message1 = await NextRawAsync(ct).ConfigureAwait(false);

        if (responder.TryHandleMessage1(message1, out var message2, out var why1) is false)
        {
            _log.Warning($"{pair.DisplayName} : message 1 refusé, {why1}");
            return null;
        }

        await _link.SendAsync(0, message2!, ct).ConfigureAwait(false);

        var message3 = await NextRawAsync(ct).ConfigureAwait(false);

        if (responder.TryHandleMessage3(message3, key => Authorizes(pair, key, _groups), out var keys, out var why2) is false)
        {
            _log.Warning($"{pair.DisplayName} : message 3 refusé, {why2}");
            return null;
        }

        return keys;
    }

    /// <summary>
    /// La clé reçue doit être celle du pair attendu, et de personne d'autre.
    /// </summary>
    /// <remarks>
    /// Pour une paire, l'empreinte de la clé sert de comparaison : une clé
    /// substituée donne une autre empreinte, donc un refus. Pour un pair de
    /// groupe, on ne connaît pas sa clé d'avance : c'est le groupe qui décide,
    /// par l'épinglage du premier vu. Sans groupe pour trancher, on refuse.
    /// </remarks>
    private static bool Authorizes(PairRecord pair, byte[] publicKey, IGroupGate? groups)
    {
        if (pair.Group is not null)
            return groups?.Admits(pair, publicKey) ?? false;

        if (PeerId.Of(publicKey) != pair.Id)
            return false;

        return pair.PublicKey is not { } known || known.AsSpan().SequenceEqual(publicKey);
    }

    private async Task<byte[]> NextRawAsync(CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));

        return await _rawIncoming.Reader.ReadAsync(deadline.Token).ConfigureAwait(false);
    }

    private void OnReceived(byte channel, byte[] payload)
    {
        // Avant l'établissement, les trames sont celles du handshake et passent
        // en clair ; après, tout est scellé. Le test et le rangement se font sous
        // le même verrou que le passage au mode scellé : sinon une trame lue
        // « avant » pourrait être rangée « après », dans une file que plus
        // personne ne lit.
        lock (_sealing)
        {
            if (_secure is null)
            {
                _rawIncoming.Writer.TryWrite(payload);
                return;
            }

            Open(_secure, payload);
        }
    }

    /// <summary>
    /// Passe au mode scellé, et reprend ce qui est arrivé trop tôt.
    /// </summary>
    /// <remarks>
    /// L'initiateur termine le handshake dès son dernier message envoyé et
    /// envoie aussitôt son premier message scellé. Chez le répondeur, ce message
    /// peut arriver avant la vérification du dernier message du handshake : il
    /// était alors rangé avec les trames du handshake, que plus personne ne lit,
    /// et le répondeur ne recevait jamais l'identité de l'autre ni son
    /// manifeste. Vu dans les tests du moteur sous charge, une fois sur trois ;
    /// reproduit à coup sûr par PeerSessionTests.
    /// </remarks>
    private void Seal(SecureChannel secure)
    {
        lock (_sealing)
        {
            _secure = secure;

            while (_rawIncoming.Reader.TryRead(out var early))
                Open(secure, early);
        }
    }

    private void Open(SecureChannel secure, byte[] payload)
    {
        if (secure.TryOpen(payload, out var kind, out var fromChannel, out var plain, out var rejection) is false)
        {
            _log.Warning($"{Pair.DisplayName} : trame refusée, {rejection}");
            return;
        }

        _incoming.Writer.TryWrite(new PeerMessage(kind, fromChannel, plain));
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        // Le lien pousse déjà dans la file : cette boucle ne sert qu'à fermer la
        // file quand le lien tombe, pour que les lecteurs se débloquent.
        while (ct.IsCancellationRequested is false && _link.IsOpen)
            await Task.Delay(500, ct).ConfigureAwait(false);

        _incoming.Writer.TryComplete();
    }

    public async ValueTask DisposeAsync()
    {
        State = PeerSessionState.Disconnected;
        _incoming.Writer.TryComplete();
        _rawIncoming.Writer.TryComplete();
        await _link.DisposeAsync().ConfigureAwait(false);
    }
}
