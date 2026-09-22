using Dalamud.Plugin;
using Penumbra.Api.Enums;
using Penumbra.Api.IpcSubscribers;

namespace Linkpearl.Integration;

/// <summary>
/// Adaptateur mince vers Penumbra. Aucune logique : elle est dans Core.
/// </summary>
/// <remarks>
/// Tous ces appels prennent des index de l'ObjectTable et doivent donc partir
/// du thread du framework. L'adaptateur ne fait pas le saut lui-même : c'est à
/// l'appelant de savoir sur quel thread il est, sinon on masquerait une faute
/// de thread derrière une couche d'indirection.
/// </remarks>
public sealed class PenumbraIpc : IDisposable
{
    /// <summary>Marque nos modifications temporaires, pour pouvoir les retirer.</summary>
    public const string Tag = "Linkpearl";

    private readonly ApiVersion _version;
    private readonly GetGameObjectResourcePaths _resourcePaths;
    private readonly GetPlayerMetaManipulations _meta;
    private readonly CreateTemporaryCollection _createCollection;
    private readonly AssignTemporaryCollection _assignCollection;
    private readonly AddTemporaryMod _addMod;
    private readonly DeleteTemporaryCollection _deleteCollection;
    private readonly RedrawObject _redraw;
    private readonly GetCollectionForObject _collectionForObject;
    private readonly IDisposable _redrawn;

    public PenumbraIpc(IDalamudPluginInterface pi)
    {
        _version           = new ApiVersion(pi);
        _resourcePaths     = new GetGameObjectResourcePaths(pi);
        _meta              = new GetPlayerMetaManipulations(pi);
        _createCollection  = new CreateTemporaryCollection(pi);
        _assignCollection  = new AssignTemporaryCollection(pi);
        _addMod            = new AddTemporaryMod(pi);
        _deleteCollection  = new DeleteTemporaryCollection(pi);
        _redraw            = new RedrawObject(pi);
        _collectionForObject = new GetCollectionForObject(pi);

        // Le redessin est le signal qui compte : tout changement de mod
        // affectant le personnage en produit un. S'abonner aux changements de
        // réglage à la place attraperait aussi les collections qui ne nous
        // concernent pas.
        _redrawn = GameObjectRedrawn.Subscriber(pi, (_, index) => Redrawn?.Invoke(index));
    }

    /// <summary>Un objet du jeu vient d'être redessiné, avec son index.</summary>
    /// <remarks>
    /// Levé depuis le thread du jeu. Ce qui en découle ne doit donc rien faire
    /// de long ici : on se contente de signaler.
    /// </remarks>
    public event Action<int>? Redrawn;

    public void Dispose() => _redrawn.Dispose();

    /// <summary>
    /// Version de l'API, ou null si Penumbra n'est pas chargé.
    /// </summary>
    /// <remarks>
    /// Sans serveur central pour imposer une version minimale, chaque pair peut
    /// avoir la sienne. On se désactive proprement plutôt que de planter.
    /// </remarks>
    public (int Major, int Minor)? TryGetVersion()
    {
        try
        {
            return _version.Invoke();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Ressources résolues pour un objet : chemin réel vers chemins de jeu.</summary>
    public IReadOnlyDictionary<string, HashSet<string>>? ResourcePathsOf(ushort objectIndex)
        => _resourcePaths.Invoke(objectIndex)[0];

    public string MetaManipulations()
        => _meta.Invoke();

    public Guid CreateCollection(string name)
    {
        var ec = _createCollection.Invoke(Tag, name, out var id);
        if (ec is not PenumbraApiEc.Success)
            throw new InvalidOperationException($"création de collection refusée par Penumbra : {ec}");

        return id;
    }

    public void AssignCollection(Guid collection, int objectIndex)
    {
        var ec = _assignCollection.Invoke(collection, objectIndex, forceAssignment: true);
        if (ec is not PenumbraApiEc.Success and not PenumbraApiEc.NothingChanged)
            throw new InvalidOperationException($"affectation de collection refusée par Penumbra : {ec}");
    }

    /// <summary>
    /// Pose la table des remplacements.
    /// </summary>
    /// <remarks>
    /// Avec le même tag, l'appel remplace l'ensemble d'un coup au lieu de
    /// s'ajouter, ce qui évite l'état intermédiaire où le personnage porte la
    /// moitié de l'ancienne apparence et la moitié de la nouvelle.
    /// </remarks>
    public void SetTemporaryMod(Guid collection, Dictionary<string, string> pathMap, string metaManipulations)
    {
        var ec = _addMod.Invoke(Tag, collection, pathMap, metaManipulations, priority: 1000);
        if (ec is not PenumbraApiEc.Success)
            throw new InvalidOperationException($"pose des remplacements refusée par Penumbra : {ec}");
    }

    public void DeleteCollection(Guid collection)
        => _deleteCollection.Invoke(collection);

    public void Redraw(int objectIndex)
        => _redraw.Invoke(objectIndex, RedrawType.Redraw);

    /// <summary>
    /// La collection réellement appliquée à un objet.
    /// </summary>
    /// <remarks>
    /// Sert au diagnostic : une collection temporaire oubliée sur le personnage
    /// du joueur expliquerait qu'un autre plugin le juge dans un état
    /// inattendu, et il faut pouvoir le constater plutôt que le supposer.
    /// </remarks>
    public string DescribeCollection(int objectIndex)
    {
        try
        {
            var result = _collectionForObject.Invoke(objectIndex);
            return $"objet valide : {result.ObjectValid}, réglage individuel : {result.IndividualSet}, "
                 + $"collection : {result.EffectiveCollection.Name} ({result.EffectiveCollection.Id})";
        }
        catch (Exception e)
        {
            return $"interrogation impossible : {e.Message}";
        }
    }
}
