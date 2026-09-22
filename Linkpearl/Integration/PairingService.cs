using Dalamud.Plugin.Services;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Integration;

/// <summary>Ce qu'une annonce au rendez-vous a donné.</summary>
public sealed record AnnounceReport(int Pairs, int Announced, int Matched, string? Failure);

/// <summary>
/// Identité, carnet de pairs, et annonce au rendez-vous.
/// </summary>
/// <remarks>
/// Ne connecte encore rien : cette étape sert à éprouver en jeu que l'identité
/// persiste, que les codes d'invitation circulent, et que le plugin sait
/// s'annoncer. La connexion entre pairs vient après.
/// </remarks>
public sealed class PairingService : IDisposable
{
    private readonly IdentityKeyPair _identity;
    private readonly PairBook _book;
    private readonly PairBookStore _bookStore;
    private readonly RendezvousTicket _tickets;
    private readonly Configuration _configuration;
    private readonly IPluginLog _log;

    public PairingService(string root, Configuration configuration, SystemClock clock, IPluginLog log)
    {
        _configuration = configuration;
        _log = log;

        _identity = IdentityKeyPair.LoadOrCreate(new DpapiIdentityStore(Path.Combine(root, "identity.key")));
        _book = new PairBook(clock);
        _bookStore = new PairBookStore(Path.Combine(root, "pairs.json"));
        _bookStore.Load(_book);
        _tickets = new RendezvousTicket(clock);
    }

    public PeerId Id => _identity.Id;

    public PairBook Book => _book;

    public string Invitation()
        => _identity.NewInvitation(_configuration.RendezvousHost).Encode();

    /// <summary>Ajoute un pair depuis un code collé par l'utilisateur.</summary>
    public string AddFromCode(string text, string displayName)
    {
        if (PairingCode.TryParse(text, out var code, out var why) is false)
            return $"code refusé : {why}";

        if (code!.Id == _identity.Id)
            return "ce code est le vôtre.";

        if (_book.Find(code.Id) is { } existing)
            return $"déjà dans le carnet sous le nom « {existing.DisplayName} ».";

        var record = _book.Invite(code, displayName, _identity.Id);
        _book.Accept(record.Id);   // l'utilisateur a collé le code : c'est son consentement
        _bookStore.Save(_book);

        return $"« {displayName} » ajouté, rendez-vous {code.RendezvousHost}.";
    }

    public string Remove(string displayName)
    {
        var record = _book.All.FirstOrDefault(
            p => string.Equals(p.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));

        if (record is null)
            return $"aucun pair nommé « {displayName} ».";

        _book.Remove(record.Id);
        _bookStore.Save(_book);
        return $"« {displayName} » retiré.";
    }

    /// <summary>
    /// S'annonce au rendez-vous pour chaque pair actif.
    /// </summary>
    /// <remarks>
    /// Une connexion par pair : les jetons d'une paire ne doivent pas arriver
    /// par la même session que ceux d'une autre, sans quoi le serveur pourrait
    /// relier entre elles les paires d'un même utilisateur, ce que les jetons
    /// tournants cherchent précisément à éviter.
    /// </remarks>
    public async Task<AnnounceReport> AnnounceAsync(CancellationToken ct)
    {
        var active = _book.Active.ToList();

        if (active.Count == 0)
            return new AnnounceReport(0, 0, 0, "aucun pair actif dans le carnet");

        var announced = 0;
        var matched = 0;
        string? failure = null;

        foreach (var pair in active)
        {
            try
            {
                await using var client = new RendezvousClient();
                await client.ConnectAsync(pair.RendezvousHost, _configuration.RendezvousPort, ct).ConfigureAwait(false);

                var tickets = _tickets.Announce(pair.PairSecret);

                // Pas encore de candidats à offrir : cette étape ne vérifie que
                // l'annonce elle-même.
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromSeconds(3));

                announced++;

                var partner = await client.AnnounceAndWaitAsync(new Announcement(tickets, []), deadline.Token)
                                          .ConfigureAwait(false);

                if (partner is not null)
                    matched++;
            }
            catch (OperationCanceledException)
            {
                // Délai écoulé : le pair n'est pas là, ce qui est le cas normal.
            }
            catch (Exception e)
            {
                failure ??= $"{pair.DisplayName} : {e.Message}";
                _log.Warning(e, $"Annonce en échec pour {pair.DisplayName}.");
            }
        }

        return new AnnounceReport(active.Count, announced, matched, failure);
    }

    public void Dispose() => _identity.Dispose();
}
