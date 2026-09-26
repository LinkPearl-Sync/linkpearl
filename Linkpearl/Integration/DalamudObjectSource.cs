using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Linkpearl.Core.Abstractions;

namespace Linkpearl.Integration;

/// <summary>Un joueur visible, avec son nom tel que le jeu l'affiche.</summary>
/// <param name="WorldName">Le nom du monde, lu dans les feuilles du jeu ; l'identifiant s'il n'y est pas.</param>
public sealed record NearbyPlayer(
    GameObjectRef Object, PlayerFingerprint Fingerprint, string Name, ushort WorldId, string WorldName)
{
    /// <summary>« Nom@Monde », comme le jeu l'écrit. Vu le 26 septembre : « @97 » ne parlait à personne.</summary>
    public string Display => $"{Name}@{WorldName}";
}

/// <summary>
/// Lit les joueurs visibles.
/// </summary>
/// <remarks>
/// <b>C'est ici, et nulle part ailleurs, que les noms de personnage sont
/// manipulés en clair.</b> Le noyau ne reçoit que des empreintes, ce qui rend
/// structurellement impossible qu'un nom parte dans une trame ou un journal.
/// Le nom ne ressort d'ici que pour l'interface, et pour la demande de pairage
/// où il est précisément ce que le destinataire doit lire.
///
/// L'ObjectTable n'est lisible que depuis le thread du framework : chaque appel
/// part de là, et l'instantané rendu est une copie que l'on peut ensuite
/// manipuler ailleurs.
/// </remarks>
public sealed class DalamudObjectSource(IObjectTable objects, IClientState clientState, IFramework framework)
{
    /// <summary>Instantané des joueurs visibles, hors soi-même.</summary>
    public Task<IReadOnlyList<NearbyPlayer>> SnapshotAsync(CancellationToken ct)
        => framework.RunOnFrameworkThread<IReadOnlyList<NearbyPlayer>>(() =>
        {
            var local = objects.LocalPlayer;
            var nearby = new List<NearbyPlayer>();

            foreach (var entry in objects)
            {
                if (entry is not IPlayerCharacter player || player.Address == local?.Address)
                    continue;

                var name = player.Name.TextValue;

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var world = (ushort)player.HomeWorld.RowId;

                nearby.Add(new NearbyPlayer(
                    new GameObjectRef(player.ObjectIndex, player.GameObjectId),
                    PlayerFingerprint.Of(Normalize(name), world),
                    name,
                    world,
                    WorldName(player)));
            }

            return nearby;
        });

    /// <summary>Notre propre personnage, ou null hors du jeu.</summary>
    public Task<NearbyPlayer?> LocalAsync(CancellationToken ct)
        => framework.RunOnFrameworkThread(() =>
        {
            if (objects.LocalPlayer is not { } player)
                return null;

            var name = player.Name.TextValue;

            if (string.IsNullOrWhiteSpace(name))
                return null;

            var world = (ushort)player.HomeWorld.RowId;

            return new NearbyPlayer(
                new GameObjectRef(player.ObjectIndex, player.GameObjectId),
                PlayerFingerprint.Of(Normalize(name), world),
                name,
                world,
                WorldName(player));
        });

    public bool IsLoggedIn => clientState.IsLoggedIn;

    /// <summary>Le nom du monde d'origine. Depuis le thread du framework, comme toute lecture d'un joueur.</summary>
    private static string WorldName(IPlayerCharacter player)
        => player.HomeWorld.ValueNullable?.Name.ExtractText() is { Length: > 0 } name
               ? name
               : player.HomeWorld.RowId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Forme du nom qui entre dans l'empreinte.
    /// </summary>
    /// <remarks>
    /// Les deux côtés doivent normaliser de la même façon, sans quoi deux
    /// joueurs calculeraient deux adresses de boîte différentes pour le même
    /// personnage et ne se trouveraient jamais.
    /// </remarks>
    internal static string Normalize(string name) => name.Trim().ToLowerInvariant();
}
