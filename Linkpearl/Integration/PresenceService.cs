using System.Collections.Concurrent;
using System.Security.Cryptography;
using Dalamud.Plugin.Services;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Sync;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Integration;

/// <summary>Une demande de pairage reçue, en attente de décision.</summary>
public sealed record IncomingRequest(
    PeerId Id, byte[] PublicKey, byte[] PairingNonce, string CharacterName, ushort WorldId,
    DateTimeOffset ReceivedAt);

/// <summary>
/// Présence au rendez-vous, détection des joueurs alentour, demandes de pairage.
/// </summary>
/// <remarks>
/// Une seule connexion, tenue ouverte. C'est elle qui vaut présence : la fermer
/// vaut déclaration d'absence, sans battement de cœur à gérer. C'est aussi elle
/// qui reçoit les demandes, poussées par le serveur plutôt que sondées.
///
/// Ce que le rendez-vous apprend, et c'est assumé : quels noms de personnage
/// sont en ligne. Une adresse de boîte dérive du nom, donc un inconnu ne peut
/// pas reconnaître quelqu'un sans que le serveur le puisse aussi. Voir
/// docs/pairage.md.
/// </remarks>
public sealed class PresenceService : IDisposable
{
    private readonly Configuration _configuration;
    private readonly IdentityKeyPair _identity;
    private readonly IPluginLog _log;
    private readonly IClock _clock;

    private readonly ConcurrentQueue<IncomingRequest> _incoming = new();
    private readonly ConcurrentDictionary<PlayerFingerprint, DateTimeOffset> _detected = new();

    private RendezvousClient? _client;
    private CancellationTokenSource? _session;
    private PlayerFingerprint? _openedFor;

    public PresenceService(Configuration configuration, IdentityKeyPair identity, IClock clock, IPluginLog log)
    {
        _configuration = configuration;
        _identity = identity;
        _clock = clock;
        _log = log;
    }

    public bool Connected => _client is not null;

    public string? LastFailure { get; private set; }

    /// <summary>Les empreintes reconnues comme utilisant le plugin, avec leur fraîcheur.</summary>
    public IReadOnlyDictionary<PlayerFingerprint, DateTimeOffset> Detected => _detected;

    public bool TryTakeRequest(out IncomingRequest? request)
    {
        var taken = _incoming.TryDequeue(out var value);
        request = value;
        return taken;
    }

    public IReadOnlyList<IncomingRequest> PeekRequests() => _incoming.ToArray();

    /// <summary>
    /// Ouvre nos boîtes, ou les rouvre si le personnage a changé.
    /// </summary>
    /// <remarks>
    /// Le changement de personnage compte : les boîtes dérivent du nom, donc
    /// celles de l'ancien doivent se fermer et celles du nouveau s'ouvrir.
    /// </remarks>
    public async Task EnsureOpenAsync(PlayerFingerprint fingerprint, CancellationToken ct)
    {
        if (_configuration.Discoverable is false)
        {
            Close();
            return;
        }

        if (_client is not null && _openedFor == fingerprint)
            return;

        Close();

        try
        {
            var client = new RendezvousClient();
            await client.ConnectAsync(_configuration.RendezvousHost, _configuration.RendezvousPort, ct)
                        .ConfigureAwait(false);

            client.Delivered += OnDelivered;

            var addresses = MailboxAddress.Around(fingerprint, _clock.UtcNow)
                .Select(a => a.ToBytes())
                .ToList();

            await client.OpenMailboxesAsync(addresses, ct).ConfigureAwait(false);

            _session = new CancellationTokenSource();
            _client = client;
            _openedFor = fingerprint;
            LastFailure = null;

            _ = Task.Run(() => ListenAsync(client, _session.Token), _session.Token);
        }
        catch (Exception e)
        {
            LastFailure = e.Message;
            _log.Warning(e, "Ouverture des boîtes en échec.");
        }
    }

    /// <summary>Demande au rendez-vous lesquels de ces joueurs utilisent le plugin.</summary>
    public async Task RefreshDetectionAsync(IReadOnlyList<NearbyPlayer> nearby, CancellationToken ct)
    {
        if (_client is null || nearby.Count == 0)
            return;

        // Le serveur plafonne les interrogations : on découpe plutôt que de se
        // faire refuser, et on ne demande rien pour une zone déserte.
        foreach (var batch in nearby.Chunk(RendezvousWire.MaxQueriedAddresses))
        {
            var addresses = batch
                .Select(p => MailboxAddress.Of(p.Fingerprint, _clock.UtcNow).ToBytes())
                .ToList();

            try
            {
                var present = await _client.QueryPresenceAsync(addresses, ct).ConfigureAwait(false);

                if (present is null)
                    return;

                for (var i = 0; i < batch.Length && i < present.Length; i++)
                {
                    if (present[i])
                        _detected[batch[i].Fingerprint] = _clock.UtcNow;
                    else
                        _detected.TryRemove(batch[i].Fingerprint, out _);
                }
            }
            catch (Exception e)
            {
                LastFailure = e.Message;
                _log.Warning(e, "Interrogation de présence en échec.");
                return;
            }
        }
    }

    /// <summary>Dépose une demande de pairage dans la boîte d'un joueur.</summary>
    public async Task<string> RequestPairAsync(NearbyPlayer target, NearbyPlayer self, CancellationToken ct)
    {
        if (_client is null)
            return "pas connecté au rendez-vous.";

        var nonce = RandomNumberGenerator.GetBytes(PairRequestMessage.NonceLength);

        var message = new PairRequestMessage(
            IsAccept: false, _identity.PublicKey, nonce, self.Name, self.WorldId);

        try
        {
            await _client.DepositAsync(
                MailboxAddress.Of(target.Fingerprint, _clock.UtcNow).ToBytes(), message.Encode(), ct)
                .ConfigureAwait(false);

            PendingOutgoing[target.Fingerprint] = nonce;
            return $"demande envoyée à {target.Name}.";
        }
        catch (Exception e)
        {
            _log.Warning(e, "Dépôt de demande en échec.");
            return $"envoi impossible : {e.Message}";
        }
    }

    /// <summary>Les aléas des demandes que nous avons envoyées, en attente de réponse.</summary>
    public ConcurrentDictionary<PlayerFingerprint, byte[]> PendingOutgoing { get; } = new();

    /// <summary>Accepte une demande reçue et renvoie notre identité au demandeur.</summary>
    public async Task<string> AcceptAsync(IncomingRequest request, NearbyPlayer self, CancellationToken ct)
    {
        if (_client is null)
            return "pas connecté au rendez-vous.";

        var reply = new PairRequestMessage(
            IsAccept: true, _identity.PublicKey, request.PairingNonce, self.Name, self.WorldId);

        var theirFingerprint = PlayerFingerprint.Of(request.CharacterName.Trim().ToLowerInvariant(), request.WorldId);

        try
        {
            await _client.DepositAsync(
                MailboxAddress.Of(theirFingerprint, _clock.UtcNow).ToBytes(), reply.Encode(), ct)
                .ConfigureAwait(false);

            return $"{request.CharacterName} accepté.";
        }
        catch (Exception e)
        {
            _log.Warning(e, "Réponse d'acceptation en échec.");
            return $"réponse impossible : {e.Message}";
        }
    }

    private void OnDelivered(byte[] payload)
    {
        if (PairRequestMessage.TryDecode(payload, out var message, out var why) is false)
        {
            _log.Warning($"Demande illisible reçue : {why}");
            return;
        }

        var id = PeerId.Of(message!.PublicKey);

        if (id == _identity.Id)
            return;   // notre propre écho, sans intérêt

        _incoming.Enqueue(new IncomingRequest(
            id, message.PublicKey, message.PairingNonce, message.CharacterName, message.WorldId, _clock.UtcNow));

        _log.Information($"Demande de pairage reçue de {message.CharacterName}.");
    }

    private async Task ListenAsync(RendezvousClient client, CancellationToken ct)
    {
        try
        {
            await client.ListenAsync(ct).ConfigureAwait(false);
        }
        catch (Exception e) when (ct.IsCancellationRequested is false)
        {
            LastFailure = e.Message;
            _log.Warning(e, "Écoute du rendez-vous interrompue.");
        }
        finally
        {
            if (ct.IsCancellationRequested is false)
                Close();
        }
    }

    private void Close()
    {
        _session?.Cancel();
        _session?.Dispose();
        _session = null;

        if (_client is not null)
        {
            _client.Delivered -= OnDelivered;
            _ = _client.DisposeAsync();
            _client = null;
        }

        _openedFor = null;
        _detected.Clear();
    }

    public void Dispose() => Close();
}
