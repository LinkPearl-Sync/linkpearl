using Linkpearl.Core.Cache;

namespace Linkpearl.Core.Manifest;

/// <summary>Ce qui permet de dire qu'un fichier n'a pas bougé.</summary>
/// <remarks>
/// La taille et la date de modification, et pas le contenu : c'est précisément
/// ce qu'on cherche à éviter de relire. Deux fichiers différents de même taille
/// et de même date existent en théorie, mais il faudrait les avoir écrits
/// exprès, et le prix de l'erreur serait d'annoncer une apparence périmée.
/// </remarks>
public readonly record struct FileStamp(long Size, DateTimeOffset Modified);

/// <summary>
/// Mémorise l'empreinte des fichiers déjà hachés.
/// </summary>
/// <remarks>
/// Un redessin arrive à chaque changement de zone et à chaque changement de
/// tenue, et chacun déclenche une reconstruction de l'apparence. Sans ce cache,
/// chacune rehacherait huit cents mégaoctets pour aboutir au même manifeste.
///
/// Il vit en mémoire et se perd au rechargement du plugin : la première
/// reconstruction d'une session coûte donc son prix, et les suivantes sont
/// presque gratuites. Le persister sur disque demanderait de se défier de son
/// propre contenu, ce qui coûterait plus cher que le gain.
/// </remarks>
public sealed class FileHashCache(int max = 8192)
{
    private readonly Dictionary<string, (FileStamp Stamp, BlobHash Hash)> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    public int Count => _entries.Count;

    public bool TryGet(string path, FileStamp stamp, out BlobHash hash)
    {
        hash = default;

        if (_entries.TryGetValue(path, out var entry) is false || entry.Stamp != stamp)
            return false;

        hash = entry.Hash;
        return true;
    }

    public void Remember(string path, FileStamp stamp, BlobHash hash)
    {
        // Vidé d'un coup plutôt qu'évincé un par un : le cache se remplit en une
        // reconstruction, donc une éviction fine ne ferait que retarder le même
        // moment, pour du code à maintenir en plus.
        if (_entries.Count >= max && _entries.ContainsKey(path) is false)
            _entries.Clear();

        _entries[path] = (stamp, hash);
    }
}
