using System.Diagnostics;
using Dalamud.Plugin.Services;
using Linkpearl.Core.Cache;
using Linkpearl.Core.Manifest;
using Linkpearl.Core.Safety;

namespace Linkpearl.Integration;

/// <summary>Ce qu'une capture a produit, pour l'afficher à l'utilisateur.</summary>
public sealed record CaptureReport(
    int FileCount, long TotalBytes, int SwapCount, int SkippedCount,
    int GamePathCount, int ManifestBytes, int CompressedManifestBytes,
    long HashMilliseconds, long CopyMilliseconds,
    IReadOnlyList<SkippedFile> Skipped);

/// <summary>
/// Boucle locale du jalon 1 : capturer son propre personnage, puis se le
/// réappliquer depuis le cache. Aucun réseau.
/// </summary>
/// <remarks>
/// Son but n'est pas d'être utile mais de lever les inconnues qui changeraient
/// la conception : sur quel thread chaque appel IPC doit partir, combien de
/// fichiers et d'octets pèse un personnage réel, combien d'échanges de fichier
/// il comporte, et surtout si Penumbra accepte un fichier nommé par son hash
/// sans extension. Si la réponse est non, l'extension devient une donnée
/// fournie par le pair, ce qui a une conséquence de sécurité directe.
/// </remarks>
public sealed class SelfLoop : IDisposable
{
    /// <summary>Le personnage joueur est toujours à l'index 0 de l'ObjectTable.</summary>
    private const int PlayerIndex = 0;

    private readonly PenumbraIpc _penumbra;
    private readonly GlamourerIpc _glamourer;
    private readonly IFramework _framework;
    private readonly IPluginLog _log;
    private readonly string _root;

    private Guid? _collection;

    public SelfLoop(PenumbraIpc penumbra, GlamourerIpc glamourer, IFramework framework, IPluginLog log)
    {
        _penumbra  = penumbra;
        _glamourer = glamourer;
        _framework = framework;
        _log       = log;

        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Linkpearl");
    }

    private string ManifestPath => Path.Combine(_root, "capture.json.br");

    /// <summary>
    /// Identifiant de la collection temporaire en cours, sur le disque.
    /// </summary>
    /// <remarks>
    /// Un rechargement du plugin perd l'état en mémoire, mais pas la collection
    /// posée dans Penumbra : sans cette trace, elle resterait affectée au
    /// personnage et plus rien ne saurait la retirer. C'est ce qui laisse un
    /// personnage bloqué jusqu'au redémarrage du jeu.
    /// </remarks>
    private string CollectionMarkerPath => Path.Combine(_root, "collection.id");

    /// <summary>
    /// Trace de ce que nous avons appliqué à Glamourer.
    /// </summary>
    /// <remarks>
    /// Sans elle, le nettoyage appellerait RevertState sur un personnage auquel
    /// nous n'avons jamais touché, ce qui efface le design que l'utilisateur
    /// avait posé lui-même. On ne défait que ce que l'on a fait.
    /// </remarks>
    private string GlamourerMarkerPath => Path.Combine(_root, "glamourer.applied");
    private string BlobDir => Path.Combine(_root, "cache", "blobs");

    public async Task<CaptureReport> CaptureAsync(CancellationToken ct) => await CaptureAsync(force: false, ct).ConfigureAwait(false);

    public async Task<CaptureReport> CaptureAsync(bool force, CancellationToken ct)
    {
        // Les appels IPC prennent des index de l'ObjectTable : ils partent du
        // thread du framework, et rien d'autre ne s'y fait.
        var (resources, meta, glamourerState) = await _framework.RunOnFrameworkThread(() =>
        (
            _penumbra.ResourcePathsOf(PlayerIndex),
            _penumbra.MetaManipulations(),
            _glamourer.StateOf(PlayerIndex)
        )).ConfigureAwait(false);

        if (resources is null)
            throw new InvalidOperationException("Penumbra n'a rendu aucune ressource pour le personnage.");

        var classified = ResourcePathClassifier.Classify(
            resources.Select(kv => (kv.Key, (IReadOnlyCollection<string>)kv.Value)), Quotas.Default);

        // Hachage et copies hors du thread de jeu : plusieurs centaines de Mo
        // passent ici, et une seule frame bloquée se voit.
        var hashWatch = Stopwatch.StartNew();
        var resolved = new List<ResolvedFile>();
        var hashes = new Dictionary<string, (BlobHash Hash, long Size)>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in classified.Files)
        {
            if (File.Exists(file.LocalPath) is false)
            {
                _log.Warning($"Fichier annoncé par Penumbra mais absent du disque : {file.LocalPath}");
                continue;
            }

            if (hashes.TryGetValue(file.LocalPath, out var entry) is false)
            {
                await using var stream = File.OpenRead(file.LocalPath);
                entry = (await BlobHash.OfStreamAsync(stream, ct).ConfigureAwait(false), stream.Length);
                hashes[file.LocalPath] = entry;
            }

            foreach (var gamePath in file.GamePaths)
                resolved.Add(new ResolvedFile(gamePath, entry.Hash, entry.Size));
        }
        hashWatch.Stop();

        var build = ManifestBuilder.Build(resolved, meta, glamourerState, Quotas.Default);

        // Le blob porte son hash pour seul nom : vérifié en jeu le 22 septembre
        // 2026, Penumbra n'a pas besoin de l'extension d'origine. C'est ce qui
        // évite que l'extension devienne une donnée fournie par le pair.
        var copyWatch = Stopwatch.StartNew();
        foreach (var file in classified.Files)
        {
            if (hashes.TryGetValue(file.LocalPath, out var entry) is false)
                continue;

            await CopyIntoCacheAsync(file.LocalPath, entry.Hash, ct).ConfigureAwait(false);
        }
        copyWatch.Stop();

        var encoded = ManifestCodec.Encode(build.Manifest);
        var compressed = ManifestCodec.Compress(build.Manifest);

        // Une capture vide écrase silencieusement la précédente, et revient à
        // annoncer à ses pairs qu'on n'a plus aucun mod. Le cas arrive pour de
        // bon : il suffit d'avoir désactivé sa collection Penumbra le temps d'un
        // essai. On refuse, plutôt que de laisser l'utilisateur le découvrir
        // en voyant son personnage nu chez les autres.
        var wouldErasePrevious = build.Manifest.Replacements.Count == 0
                              && File.Exists(ManifestPath)
                              && HasPreviousContent();

        if (wouldErasePrevious && force is false)
            throw new InvalidOperationException(
                "aucune ressource moddée trouvée, alors qu'une capture précédente en contenait. "
              + "Vos mods sont-ils activés dans Penumbra ? Utilisez « capture force » pour écraser quand même.");

        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(ManifestPath, compressed, ct).ConfigureAwait(false);

        return new CaptureReport(
            FileCount: hashes.Count,
            TotalBytes: hashes.Values.Sum(e => e.Size),
            SwapCount: classified.Swaps.Count,
            SkippedCount: classified.Skipped.Count,
            GamePathCount: build.Manifest.Replacements.Sum(r => r.GamePaths.Count),
            ManifestBytes: encoded.Length,
            CompressedManifestBytes: compressed.Length,
            HashMilliseconds: hashWatch.ElapsedMilliseconds,
            CopyMilliseconds: copyWatch.ElapsedMilliseconds,
            Skipped: classified.Skipped.Concat(build.Skipped).ToList());
    }

    /// <summary>Vrai si le manifeste déjà enregistré contient quelque chose.</summary>
    private bool HasPreviousContent()
    {
        try
        {
            var previous = File.ReadAllBytes(ManifestPath);
            return ManifestCodec.TryDecompress(previous, Quotas.Default, out var manifest, out _)
                && manifest!.Replacements.Count > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task CopyIntoCacheAsync(string source, BlobHash hash, CancellationToken ct)
    {
        var directory = Path.Combine(BlobDir, hash.CacheLevel1, hash.CacheLevel2);
        Directory.CreateDirectory(directory);

        var destination = Path.Combine(directory, hash.ToHex());
        if (File.Exists(destination))
            return;

        // Écriture en .part puis déplacement : un arrêt brutal ne doit jamais
        // laisser un blob visible et tronqué, que la taille seule validerait.
        var part = destination + ".part";
        await using (var input = File.OpenRead(source))
        await using (var output = File.Create(part))
            await input.CopyToAsync(output, ct).ConfigureAwait(false);

        File.Move(part, destination, overwrite: false);
    }

    /// <summary>
    /// Réapplique le manifeste capturé, depuis le cache et non depuis les
    /// fichiers d'origine : c'est le chemin réel qu'un pair emprunterait.
    /// </summary>
    public async Task<int> ApplyAsync(CancellationToken ct)
    {
        var compressed = await File.ReadAllBytesAsync(ManifestPath, ct).ConfigureAwait(false);

        if (ManifestCodec.TryDecompress(compressed, Quotas.Default, out var manifest, out var why) is false)
            throw new InvalidOperationException($"manifeste illisible : {why}");

        if (ManifestValidator.TryAccept(manifest!, Quotas.Default, out var refus) is false)
            throw new InvalidOperationException($"manifeste refusé : {refus}");

        var pathMap = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var replacement in manifest!.Replacements)
        {
            var blob = Path.Combine(
                BlobDir, replacement.Hash.CacheLevel1, replacement.Hash.CacheLevel2, replacement.Hash.ToHex());

            if (File.Exists(blob) is false)
                continue;

            foreach (var gamePath in replacement.GamePaths)
                pathMap[gamePath] = blob;
        }

        var state = manifest.GlamourerState;

        await _framework.RunOnFrameworkThread(() =>
        {
            if (_collection is null)
            {
                _collection = _penumbra.CreateCollection("Linkpearl self-test");
                RememberCollection(_collection.Value);
            }

            _penumbra.AssignCollection(_collection.Value, PlayerIndex);
            _penumbra.SetTemporaryMod(_collection.Value, pathMap, manifest.MetaManipulations);
            _penumbra.Redraw(PlayerIndex);
        }).ConfigureAwait(false);

        if (state is not null)
        {
            // Glamourer après le redessin, jamais avant : l'inverse donne un état
            // écrasé par l'automation du receveur.
            await _framework.RunOnFrameworkThread(() => _glamourer.ApplyState(state, PlayerIndex))
                            .ConfigureAwait(false);

            RememberGlamourerApplied();
        }

        return pathMap.Count;
    }

    /// <summary>
    /// Rend le personnage à son état normal, y compris après un rechargement du
    /// plugin qui aurait perdu l'état en mémoire.
    /// </summary>
    public void Revert()
    {
        var collections = new[] { _collection, ReadRememberedCollection() }.OfType<Guid>().Distinct().ToList();
        var touchedGlamourer = File.Exists(GlamourerMarkerPath);

        // Rien posé, rien à défaire. C'est le cas de loin le plus fréquent, et
        // toucher au personnage dans ce cas effacerait le travail de
        // l'utilisateur.
        if (collections.Count == 0 && touchedGlamourer is false)
            return;

        if (touchedGlamourer)
        {
            try
            {
                _glamourer.Release(PlayerIndex);
            }
            catch (Exception e)
            {
                _log.Warning(e, "Relâchement de l'état Glamourer en échec.");
            }

            ForgetGlamourer();
        }

        foreach (var collection in collections)
        {
            try
            {
                _penumbra.DeleteCollection(collection);
            }
            catch (Exception e)
            {
                _log.Warning(e, $"Suppression de la collection {collection} en échec.");
            }
        }

        _collection = null;
        ForgetCollection();

        try
        {
            _penumbra.Redraw(PlayerIndex);
        }
        catch (Exception e)
        {
            _log.Warning(e, "Redessin en échec.");
        }
    }

    /// <summary>Vrai s'il reste quelque chose à nettoyer d'une session précédente.</summary>
    public bool HasLeftovers() => ReadRememberedCollection() is not null || File.Exists(GlamourerMarkerPath);

    private void RememberGlamourerApplied()
    {
        try
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(GlamourerMarkerPath, "1");
        }
        catch (Exception e)
        {
            _log.Warning(e, "Trace Glamourer non écrite.");
        }
    }

    private void ForgetGlamourer()
    {
        try
        {
            File.Delete(GlamourerMarkerPath);
        }
        catch (Exception)
        {
            // Sans conséquence : relâcher un verrou absent ne fait rien.
        }
    }

    private void RememberCollection(Guid collection)
    {
        try
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(CollectionMarkerPath, collection.ToString("D"));
        }
        catch (Exception e)
        {
            _log.Warning(e, "Trace de collection non écrite : un rechargement laisserait la collection en place.");
        }
    }

    private Guid? ReadRememberedCollection()
    {
        try
        {
            return File.Exists(CollectionMarkerPath) && Guid.TryParse(File.ReadAllText(CollectionMarkerPath), out var id)
                ? id
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ForgetCollection()
    {
        try
        {
            File.Delete(CollectionMarkerPath);
        }
        catch (Exception)
        {
            // Sans conséquence : la suppression de collection est idempotente.
        }
    }

    public void Dispose()
    {
        // Revert ne touche au personnage que si nous y avons posé quelque chose.
        // Une collection temporaire oubliée casse un personnage jusqu'au
        // redémarrage du jeu ; un RevertState sur un personnage auquel on n'a
        // pas touché efface le design de l'utilisateur. Les deux sont à éviter.
        Revert();
    }
}
