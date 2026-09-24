using Dalamud.Plugin.Services;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Integration;

/// <summary>
/// Télécharge la liste de bannissement de chaque service actif, puis toutes les heures.
/// </summary>
/// <remarks>
/// Une connexion neuve par service et par téléchargement, fermée aussitôt :
/// la présence tient déjà la sienne, et y mêler ces trames ferait deux
/// lecteurs sur un flux. Un échec garde la liste précédente : un service qui
/// hoquette ne doit pas lever d'un coup tous ses bannissements. Un service
/// d'avant ces trames n'a jamais de liste, et ne bannit donc personne.
/// </remarks>
public sealed class ServiceBanFetcher(Configuration configuration, ServiceBanBook book, IPluginLog log) : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly CancellationTokenSource _life = new();
    private readonly SemaphoreSlim _wake = new(0, 1);

    public void Start() => _ = Task.Run(() => LoopAsync(_life.Token));

    /// <summary>La liste des services a changé : ne pas attendre l'heure.</summary>
    public void RefreshSoon()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // Déjà réveillé : une seconde demande n'ajoute rien.
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (ct.IsCancellationRequested is false)
        {
            var services = configuration.ActiveRendezvous.Select(entry => entry.Address).ToList();
            book.Retain(services);

            foreach (var service in services)
                await FetchAsync(service, ct).ConfigureAwait(false);

            try
            {
                await _wake.WaitAsync(Interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task FetchAsync(RendezvousAddress service, CancellationToken ct)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));

            await using var client = new RendezvousClient();
            await client.ConnectAsync(service.Host, service.Port, deadline.Token).ConfigureAwait(false);

            var (list, failure) = await client.QueryBanListAsync(deadline.Token).ConfigureAwait(false);

            if (list is null)
            {
                log.Debug($"Liste de bannissement de {service} indisponible : {failure}");
                return;
            }

            book.SetList(service, list);
            log.Information($"Liste de bannissement de {service} : {list.Entries.Count} entrée(s).");
        }
        catch (Exception e) when (ct.IsCancellationRequested is false)
        {
            log.Debug($"Liste de bannissement de {service} en échec : {e.Message}");
        }
    }

    public void Dispose()
    {
        _life.Cancel();
        _life.Dispose();
    }
}
