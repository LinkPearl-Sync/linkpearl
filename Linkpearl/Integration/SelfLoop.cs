using Dalamud.Plugin.Services;

namespace Linkpearl.Integration;

/// <summary>
/// Retire ce que l'ancienne boucle locale du jalon 1 a pu laisser sur notre
/// propre personnage.
/// </summary>
/// <remarks>
/// La boucle elle-même n'existe plus : elle se capturait puis se réappliquait
/// depuis le cache, le temps de lever les inconnues côté jeu. Mais une
/// installation qui l'a lancée peut garder une collection temporaire ou un état
/// Glamourer posé, dont seules les traces sur le disque disent l'existence.
/// Sans ce nettoyage, le personnage resterait bloqué jusqu'au redémarrage du jeu.
/// </remarks>
public sealed class SelfLoop : IDisposable
{
    /// <summary>Le personnage joueur est toujours à l'index 0 de l'ObjectTable.</summary>
    private const int PlayerIndex = 0;

    private readonly PenumbraIpc _penumbra;
    private readonly GlamourerIpc _glamourer;
    private readonly IPluginLog _log;
    private readonly string _root;

    public SelfLoop(PenumbraIpc penumbra, GlamourerIpc glamourer, IPluginLog log)
    {
        _penumbra  = penumbra;
        _glamourer = glamourer;
        _log       = log;

        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Linkpearl");
    }

    /// <summary>
    /// Identifiant de la collection temporaire posée, sur le disque.
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

    /// <summary>
    /// Rend le personnage à son état normal, y compris après un rechargement du
    /// plugin qui aurait perdu l'état en mémoire.
    /// </summary>
    public void Revert()
    {
        var collection = ReadRememberedCollection();
        var touchedGlamourer = File.Exists(GlamourerMarkerPath);

        // Rien posé, rien à défaire. C'est le cas de loin le plus fréquent, et
        // toucher au personnage dans ce cas effacerait le travail de
        // l'utilisateur.
        if (collection is null && touchedGlamourer is false)
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

        if (collection is { } id)
        {
            try
            {
                _penumbra.DeleteCollection(id);
            }
            catch (Exception e)
            {
                _log.Warning(e, $"Suppression de la collection {id} en échec.");
            }
        }

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
