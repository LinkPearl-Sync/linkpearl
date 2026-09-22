using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;

namespace Linkpearl.Core.Cache;

/// <summary>
/// Cache adressé par contenu, sur le système de fichiers.
/// </summary>
/// <remarks>
/// Deux niveaux de 256 répertoires : à cent mille blobs, environ un fichier et
/// demi par feuille, ce qui garde l'énumération rapide sur NTFS.
///
/// Le répertoire d'arrivée partage le volume des blobs, sans quoi
/// <see cref="File.Move(string,string,bool)"/> cesserait d'être atomique et un
/// blob tronqué pourrait devenir visible.
///
/// La récence est tenue en mémoire et non lue sur le disque : l'horodatage de
/// dernier accès de NTFS est désactivé par défaut depuis Vista, s'y fier
/// donnerait une éviction arbitraire.
/// </remarks>
public sealed class FileSystemBlobStore : IBlobStore
{
    private sealed record Entry(long Size, DateTimeOffset LastUsed);

    private readonly string _root;
    private readonly CacheSettings _settings;
    private readonly IClock _clock;
    private readonly Func<string, long> _freeSpace;
    private readonly ConcurrentDictionary<BlobHash, Entry> _entries = new();

    public FileSystemBlobStore(string root, CacheSettings settings, IClock clock, Func<string, long> freeSpace)
    {
        _root = root;
        _settings = settings;
        _clock = clock;
        _freeSpace = freeSpace;

        Directory.CreateDirectory(BlobsDirectory);
        Directory.CreateDirectory(IncomingDirectory);

        if (TryLoadIndex() is false)
            Rebuild();
    }

    private string BlobsDirectory => Path.Combine(_root, "blobs");
    private string IncomingDirectory => Path.Combine(_root, "incoming");
    private string IndexPath => Path.Combine(_root, "cache.index");

    public long TotalBytes => _entries.Values.Sum(e => e.Size);

    public int Count => _entries.Count;

    public bool IsReadOnly => _freeSpace(_root) < _settings.MinimumFreeBytes;

    public string PathFor(BlobHash hash)
        => Path.Combine(BlobsDirectory, hash.CacheLevel1, hash.CacheLevel2, hash.ToHex());

    public bool TryGetSize(BlobHash hash, out long size)
    {
        if (_entries.TryGetValue(hash, out var entry))
        {
            size = entry.Size;
            return true;
        }

        size = 0;
        return false;
    }

    public void Touch(BlobHash hash)
    {
        if (_entries.TryGetValue(hash, out var entry))
            _entries[hash] = entry with { LastUsed = _clock.UtcNow };
    }

    public Task<Stream> OpenReadAsync(BlobHash hash, CancellationToken ct)
    {
        var path = PathFor(hash);

        if (File.Exists(path) is false)
            throw new FileNotFoundException($"blob absent du cache : {hash}", path);

        Touch(hash);

        // FileShare.Read : un blob ne change jamais après sa publication, donc
        // plusieurs lecteurs simultanés ne posent aucun problème.
        return Task.FromResult<Stream>(
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true));
    }

    public Task<IBlobWriter> BeginWriteAsync(BlobHash expected, long expectedSize, CancellationToken ct)
    {
        if (IsReadOnly)
            return Task.FromResult<IBlobWriter>(new RefusedWriter(
                $"espace libre insuffisant sur le volume du cache (moins de {_settings.MinimumFreeBytes} octets)"));

        if (expectedSize > _settings.QuotaBytes)
            return Task.FromResult<IBlobWriter>(new RefusedWriter(
                $"blob plus gros que le quota entier ({expectedSize} octets pour un quota de {_settings.QuotaBytes})"));

        var part = Path.Combine(IncomingDirectory, Guid.NewGuid().ToString("N") + ".part");
        return Task.FromResult<IBlobWriter>(new BlobWriter(this, expected, expectedSize, part));
    }

    public async Task EvictToAsync(long targetBytes, IReadOnlySet<BlobHash> pinned, CancellationToken ct)
    {
        var candidates = _entries
            .Where(pair => pinned.Contains(pair.Key) is false)
            .OrderBy(pair => pair.Value.LastUsed)
            .ToList();

        var total = TotalBytes;

        foreach (var (hash, entry) in candidates)
        {
            if (total <= targetBytes || ct.IsCancellationRequested)
                break;

            if (_entries.TryRemove(hash, out _) is false)
                continue;

            try
            {
                File.Delete(PathFor(hash));
            }
            catch (IOException)
            {
                // Le blob est peut-être en cours de lecture : l'index l'a déjà
                // oublié, le fichier partira à la prochaine reconstruction.
            }

            total -= entry.Size;
        }

        await SaveIndexAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Seuil au-delà duquel une éviction doit être déclenchée.</summary>
    public bool NeedsEviction => TotalBytes > _settings.QuotaBytes;

    public long EvictionTarget => (long)(_settings.QuotaBytes * _settings.LowWatermark);

    /// <summary>
    /// Écrit l'index en entier.
    /// </summary>
    /// <remarks>
    /// Réécriture complète plutôt que journal : quarante octets par blob, soit
    /// deux mégaoctets pour cinquante mille blobs, ce qui ne coûte rien et évite
    /// d'avoir à écrire un journal correct.
    /// </remarks>
    public async Task SaveIndexAsync(CancellationToken ct)
    {
        var lines = _entries.Select(pair =>
            $"{pair.Key.ToHex()} {pair.Value.Size} {pair.Value.LastUsed.ToUnixTimeSeconds()}");

        var temporary = IndexPath + ".part";
        await File.WriteAllLinesAsync(temporary, lines, ct).ConfigureAwait(false);
        File.Move(temporary, IndexPath, overwrite: true);
    }

    private bool TryLoadIndex()
    {
        if (File.Exists(IndexPath) is false)
            return false;

        var loaded = 0;

        foreach (var line in File.ReadLines(IndexPath))
        {
            var parts = line.Split(' ');

            if (parts.Length != 3
                || BlobHash.TryParseHex(parts[0], out var hash) is false
                || long.TryParse(parts[1], CultureInfo.InvariantCulture, out var size) is false
                || long.TryParse(parts[2], CultureInfo.InvariantCulture, out var lastUsed) is false)
                continue;   // ligne illisible : on l'ignore, la reconstruction rattrapera

            if (File.Exists(PathFor(hash)) is false)
                continue;

            _entries[hash] = new Entry(size, DateTimeOffset.FromUnixTimeSeconds(lastUsed));
            loaded++;
        }

        // Un index vide alors que des blobs existent signale une corruption :
        // mieux vaut reconstruire que démarrer en croyant le cache vide.
        return loaded > 0 || Directory.EnumerateFiles(BlobsDirectory, "*", SearchOption.AllDirectories).Any() is false;
    }

    /// <summary>
    /// Reconstruit l'index depuis le disque.
    /// </summary>
    /// <remarks>
    /// Seuls le nom et la taille sont relus. Revérifier le hash de vingt
    /// gigaoctets à chaque démarrage serait inacceptable ; la vérification
    /// complète est faite à l'écriture, et le nom du fichier est le hash.
    /// </remarks>
    private void Rebuild()
    {
        _entries.Clear();

        foreach (var path in Directory.EnumerateFiles(BlobsDirectory, "*", SearchOption.AllDirectories))
        {
            if (BlobHash.TryParseHex(Path.GetFileName(path), out var hash) is false)
                continue;

            _entries[hash] = new Entry(new FileInfo(path).Length, _clock.UtcNow);
        }

        // Les écritures interrompues d'une session précédente ne servent plus à
        // rien : leur nom était aléatoire, on ne saurait pas les reprendre.
        foreach (var part in Directory.EnumerateFiles(IncomingDirectory, "*.part"))
        {
            try
            {
                File.Delete(part);
            }
            catch (IOException)
            {
                // sans conséquence
            }
        }
    }

    private void Publish(BlobHash hash, long size) => _entries[hash] = new Entry(size, _clock.UtcNow);

    /// <summary>Écriture refusée d'avance, pour que l'appelant n'ait qu'un chemin à gérer.</summary>
    private sealed class RefusedWriter(string reason) : IBlobWriter
    {
        public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct) => ValueTask.CompletedTask;

        public ValueTask<BlobCommitResult> CommitAsync(CancellationToken ct)
            => ValueTask.FromResult(BlobCommitResult.Refused(reason));

        public void Abort() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlobWriter(FileSystemBlobStore store, BlobHash expected, long expectedSize, string partPath)
        : IBlobWriter
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly FileStream _stream = new(partPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);

        private long _written;
        private bool _aborted;
        private bool _committed;

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
        {
            _hash.AppendData(data.Span);
            await _stream.WriteAsync(data, ct).ConfigureAwait(false);
            _written += data.Length;
        }

        public async ValueTask<BlobCommitResult> CommitAsync(CancellationToken ct)
        {
            await _stream.FlushAsync(ct).ConfigureAwait(false);
            await _stream.DisposeAsync().ConfigureAwait(false);

            if (_written != expectedSize)
            {
                Discard();
                return BlobCommitResult.Refused($"taille reçue {_written}, annoncée {expectedSize}");
            }

            var actual = BlobHash.FromBytes(_hash.GetHashAndReset());

            if (actual != expected)
            {
                Discard();
                return BlobCommitResult.Refused($"empreinte reçue {actual}, annoncée {expected}");
            }

            var destination = store.PathFor(expected);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            try
            {
                // overwrite: false, et l'échec est un succès : un autre pair vient
                // de publier le même contenu, qui est par définition identique.
                File.Move(partPath, destination, overwrite: false);
            }
            catch (IOException) when (File.Exists(destination))
            {
                Discard();
            }

            store.Publish(expected, _written);
            _committed = true;
            return BlobCommitResult.Ok;
        }

        public void Abort() => _aborted = true;

        public async ValueTask DisposeAsync()
        {
            _hash.Dispose();

            if (_committed)
                return;

            await _stream.DisposeAsync().ConfigureAwait(false);

            if (_aborted || File.Exists(partPath))
                Discard();
        }

        private void Discard()
        {
            try
            {
                if (File.Exists(partPath))
                    File.Delete(partPath);
            }
            catch (IOException)
            {
                // sans conséquence : la reconstruction nettoiera
            }
        }
    }
}
