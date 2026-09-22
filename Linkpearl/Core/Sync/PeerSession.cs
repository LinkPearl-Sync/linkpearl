using System.Security.Cryptography;
using System.Threading.Channels;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
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

    private SecureChannel? _secure;

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
        CancellationToken ct)
    {
        var session = new PeerSession(link, pair, log) { State = PeerSessionState.Handshaking };

        try
        {
            var weInitiate = string.CompareOrdinal(ourId.ToHex(), pair.Id.ToHex()) < 0;

            var keys = weInitiate
                ? await session.InitiateAsync(identity, pair, clock, ct).ConfigureAwait(false)
                : await session.RespondAsync(identity, pair, clock, ct).ConfigureAwait(false);

            if (keys is null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
                return null;
            }

            session._secure = new SecureChannel(keys.SendKey, keys.ReceiveKey, keys.SessionId);
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

    private async Task<SessionKeys?> InitiateAsync(ECDsa identity, PairRecord pair, IClock clock, CancellationToken ct)
    {
        var initiator = new HandshakeInitiator(identity, clock);

        await _link.SendAsync(0, initiator.CreateMessage1(), ct).ConfigureAwait(false);

        var message2 = await NextRawAsync(ct).ConfigureAwait(false);

        if (initiator.TryHandleMessage2(message2, key => Authorizes(pair, key), out var message3, out var keys, out var why) is false)
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

        if (responder.TryHandleMessage3(message3, key => Authorizes(pair, key), out var keys, out var why2) is false)
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
    /// L'empreinte de la clé sert de comparaison : une clé substituée donne une
    /// autre empreinte, donc un refus. La liaison est assurée par construction.
    /// </remarks>
    private static bool Authorizes(PairRecord pair, byte[] publicKey)
    {
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
        // en clair ; après, tout est scellé.
        if (_secure is null)
        {
            _rawIncoming.Writer.TryWrite(payload);
            return;
        }

        if (_secure.TryOpen(payload, out var kind, out var fromChannel, out var plain, out var rejection) is false)
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
