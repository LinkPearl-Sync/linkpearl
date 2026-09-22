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
    private string BlobDir(bool withExtension) => Path.Combine(_root, "cache", withExtension ? "blobs-ext" : "blobs");

    public async Task<CaptureReport> CaptureAsync(CancellationToken ct)
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

        // Les deux variantes de nommage, pour trancher la question en jeu.
        var copyWatch = Stopwatch.StartNew();
        foreach (var file in classified.Files)
        {
            if (hashes.TryGetValue(file.LocalPath, out var entry) is false)
                continue;

            await CopyIntoCacheAsync(file.LocalPath, entry.Hash, extension: null, ct).ConfigureAwait(false);
            await CopyIntoCacheAsync(file.LocalPath, entry.Hash, ExtensionOf(file.GamePaths[0]), ct).ConfigureAwait(false);
        }
        copyWatch.Stop();

        var encoded = ManifestCodec.Encode(build.Manifest);
        var compressed = ManifestCodec.Compress(build.Manifest);

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

    private async Task CopyIntoCacheAsync(string source, BlobHash hash, string? extension, CancellationToken ct)
    {
        var directory = Path.Combine(BlobDir(extension is not null), hash.CacheLevel1, hash.CacheLevel2);
        Directory.CreateDirectory(directory);

        var destination = Path.Combine(directory, hash.ToHex() + extension);
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
    public async Task<int> ApplyAsync(bool withExtension, CancellationToken ct)
    {
        var compressed = await File.ReadAllBytesAsync(ManifestPath, ct).ConfigureAwait(false);

        if (ManifestCodec.TryDecompress(compressed, Quotas.Default, out var manifest, out var why) is false)
            throw new InvalidOperationException($"manifeste illisible : {why}");

        if (ManifestValidator.TryAccept(manifest!, Quotas.Default, out var refus) is false)
            throw new InvalidOperationException($"manifeste refusé : {refus}");

        var pathMap = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var replacement in manifest!.Replacements)
        {
            foreach (var gamePath in replacement.GamePaths)
            {
                var extension = withExtension ? ExtensionOf(gamePath) : null;
                var blob = Path.Combine(
                    BlobDir(withExtension), replacement.Hash.CacheLevel1, replacement.Hash.CacheLevel2,
                    replacement.Hash.ToHex() + extension);

                if (File.Exists(blob))
                    pathMap[gamePath] = blob;
            }
        }

        var state = manifest.GlamourerState;

        await _framework.RunOnFrameworkThread(() =>
        {
            _collection ??= _penumbra.CreateCollection("Linkpearl self-test");
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
        }

        return pathMap.Count;
    }

    /// <summary>Rend le personnage à son état normal.</summary>
    public void Revert()
    {
        try
        {
            _glamourer.Release(PlayerIndex);

            if (_collection is { } collection)
            {
                _penumbra.DeleteCollection(collection);
                _collection = null;
            }

            _penumbra.Redraw(PlayerIndex);
        }
        catch (Exception e)
        {
            _log.Error(e, "Nettoyage incomplet.");
        }
    }

    private static string? ExtensionOf(string gamePath)
    {
        var dot = gamePath.LastIndexOf('.');
        return dot > gamePath.LastIndexOf('/') ? gamePath[dot..] : null;
    }

    public void Dispose()
    {
        // Une collection temporaire laissée derrière nous, c'est un personnage
        // cassé jusqu'au redémarrage du jeu.
        if (_collection is not null)
            Revert();
    }
}
