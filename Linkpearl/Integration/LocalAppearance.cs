using Dalamud.Plugin.Services;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Cache;
using Linkpearl.Core.Manifest;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Sync;

namespace Linkpearl.Integration;

/// <summary>
/// Notre propre apparence, telle que le moteur l'annonce aux pairs.
/// </summary>
/// <remarks>
/// Le moteur demande le manifeste courant à chaque tic. Le reconstruire là
/// serait impensable : une apparence pèse des centaines de mégaoctets à hacher.
/// Il est donc bâti en tâche de fond, et <see cref="CurrentAsync"/> rend
/// toujours la dernière version connue, sans attendre.
///
/// L'instance rendue ne change que lorsque l'apparence change pour de bon. Le
/// moteur compare par référence pour décider s'il doit réannoncer : rendre un
/// objet neuf à chaque appel ferait réannoncer à chaque tic.
/// </remarks>
public sealed class LocalAppearance : ILocalAppearance, IDisposable
{
    /// <summary>Le personnage joueur est toujours à l'index 0 de l'ObjectTable.</summary>
    private const int PlayerIndex = 0;

    private readonly PenumbraIpc _penumbra;
    private readonly GlamourerIpc _glamourer;
    private readonly IFramework _framework;
    private readonly IBlobStore _store;
    private readonly IPluginLog _log;
    private readonly CancellationTokenSource _life = new();
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    private volatile CharacterManifest? _current;
    private PlayerFingerprint? _fingerprint;

    public LocalAppearance(
        PenumbraIpc penumbra, GlamourerIpc glamourer, IFramework framework, IBlobStore store, IPluginLog log)
    {
        _penumbra = penumbra;
        _glamourer = glamourer;
        _framework = framework;
        _store = store;
        _log = log;
    }

    public PlayerFingerprint? Fingerprint => _fingerprint;

    /// <summary>Ce que la dernière construction a produit, pour l'interface.</summary>
    public string Description { get; private set; } = "rien de capturé pour l'instant";

    public bool Building { get; private set; }

    public Task<CharacterManifest?> CurrentAsync(CancellationToken ct) => Task.FromResult(_current);

    /// <summary>
    /// Change le personnage dont on annonce l'apparence.
    /// </summary>
    /// <remarks>
    /// Un changement de personnage invalide tout : les mods ne sont pas les
    /// mêmes, et annoncer l'apparence de l'ancien ferait voir aux pairs
    /// quelqu'un qui n'est pas là.
    /// </remarks>
    public void Follow(PlayerFingerprint? fingerprint)
    {
        if (_fingerprint == fingerprint)
            return;

        _fingerprint = fingerprint;
        _current = null;
        Description = fingerprint is null ? "hors du jeu" : "apparence à construire";

        if (fingerprint is not null)
            Rebuild();
    }

    /// <summary>Reconstruit l'apparence, en tâche de fond.</summary>
    /// <remarks>
    /// Une seule construction à la fois : deux hachages simultanés de plusieurs
    /// centaines de mégaoctets se disputeraient le disque pour rien.
    /// </remarks>
    public void Rebuild()
    {
        if (_life.IsCancellationRequested)
            return;

        _ = Task.Run(async () =>
        {
            if (await _oneAtATime.WaitAsync(0, _life.Token).ConfigureAwait(false) is false)
                return;

            Building = true;

            try
            {
                await RebuildAsync(_life.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Description = $"construction en échec : {e.Message}";
                _log.Warning(e, "Construction de l'apparence locale en échec.");
            }
            finally
            {
                Building = false;
                _oneAtATime.Release();
            }
        }, _life.Token);
    }

    private async Task RebuildAsync(CancellationToken ct)
    {
        // L'ObjectTable et l'IPC ne se touchent que depuis le thread du
        // framework. Ce qui suit est une copie, manipulable ailleurs.
        var (resources, meta, glamourer) = await _framework.RunOnFrameworkThread(() =>
        (
            _penumbra.ResourcePathsOf(PlayerIndex),
            _penumbra.MetaManipulations(),
            _glamourer.StateOf(PlayerIndex)
        )).ConfigureAwait(false);

        if (resources is null)
        {
            Description = "Penumbra n'a rendu aucune ressource";
            return;
        }

        var classified = ResourcePathClassifier.Classify(
            resources.Select(kv => (kv.Key, (IReadOnlyCollection<string>)kv.Value)), Quotas.Default);

        // Hachage et copies hors du thread de jeu : plusieurs centaines de Mo
        // passent ici, et une seule image bloquée se voit.
        var resolved = new List<ResolvedFile>();
        var known = new Dictionary<string, (BlobHash Hash, long Size)>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in classified.Files)
        {
            ct.ThrowIfCancellationRequested();

            if (File.Exists(file.LocalPath) is false)
            {
                // Penumbra a annoncé un fichier que le disque n'a plus. Le
                // signaler sans chemin complet : il porte le nom de l'utilisateur.
                _log.Warning($"Fichier annoncé par Penumbra mais absent du disque : {Path.GetFileName(file.LocalPath)}");
                continue;
            }

            if (known.TryGetValue(file.LocalPath, out var entry) is false)
            {
                await using (var stream = File.OpenRead(file.LocalPath))
                    entry = (await BlobHash.OfStreamAsync(stream, ct).ConfigureAwait(false), stream.Length);

                known[file.LocalPath] = entry;
                await StoreAsync(file.LocalPath, entry.Hash, entry.Size, ct).ConfigureAwait(false);
            }

            foreach (var gamePath in file.GamePaths)
                resolved.Add(new ResolvedFile(gamePath, entry.Hash, entry.Size));
        }

        var build = ManifestBuilder.Build(resolved, meta, glamourer, Quotas.Default);

        // Une apparence vide remplacerait la précédente, et reviendrait à
        // annoncer aux pairs qu'on n'a plus aucun mod. Le cas arrive pour de
        // bon : il suffit d'avoir désactivé sa collection Penumbra le temps
        // d'un essai. On garde la précédente plutôt que de laisser
        // l'utilisateur le découvrir en se voyant nu chez les autres.
        if (build.Manifest.Replacements.Count == 0 && _current is { Replacements.Count: > 0 })
        {
            Description = "aucune ressource moddée trouvée, l'apparence précédente est conservée";
            _log.Warning(Description);
            return;
        }

        _current = build.Manifest;

        Description = $"{build.Manifest.Replacements.Count} fichiers, "
                    + $"{build.Manifest.Replacements.Sum(r => r.GamePaths.Count)} chemins de jeu, "
                    + $"{known.Values.Sum(e => e.Size) / 1024 / 1024} Mo"
                    + $"{(build.Skipped.Count > 0 ? $", {build.Skipped.Count} écartés" : "")}";

        _log.Information($"Apparence locale construite : {Description}");
    }

    /// <summary>Range un fichier dans le cache, sous son hash pour seul nom.</summary>
    /// <remarks>
    /// Par le magasin et non par une copie à la main : c'est lui qui écrit à
    /// côté puis publie après vérification, de sorte qu'un arrêt brutal ne
    /// laisse jamais un blob visible et tronqué.
    /// </remarks>
    private async Task StoreAsync(string source, BlobHash hash, long size, CancellationToken ct)
    {
        if (_store.TryGetSize(hash, out _))
            return;

        await using var writer = await _store.BeginWriteAsync(hash, size, ct).ConfigureAwait(false);
        await using var input = File.OpenRead(source);

        var buffer = new byte[64 * 1024];

        while (true)
        {
            var read = await input.ReadAsync(buffer, ct).ConfigureAwait(false);

            if (read == 0)
                break;

            await writer.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        var result = await writer.CommitAsync(ct).ConfigureAwait(false);

        if (result.Accepted is false)
            _log.Warning($"Blob refusé par le cache : {result.Rejection}");
    }

    public void Dispose()
    {
        _life.Cancel();
        _life.Dispose();
        _oneAtATime.Dispose();
    }
}
