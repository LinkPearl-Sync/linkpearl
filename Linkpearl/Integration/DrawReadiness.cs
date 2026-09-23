using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using SceneObjectType = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.ObjectType;

namespace Linkpearl.Integration;

/// <summary>
/// Dit si un personnage a fini de charger ce qu'il porte.
/// </summary>
/// <remarks>
/// Penumbra rend les ressources <i>chargées</i>, pas celles qui le seront. Lues
/// pendant un redessin, elles sont incomplètes : vu en jeu, une reconstruction
/// tombée juste après une retouche Glamourer n'a trouvé qu'un fichier sur
/// soixante-quatre, et le pair a reçu les objets de base sans aucun mod.
///
/// Les quatre conditions sont celles qu'emploient les outils comparables : un
/// objet de dessin présent, aucun drapeau de rendu en attente, et aucun modèle
/// ni fichier de modèle encore en cours de chargement dans un emplacement.
///
/// Ne s'appelle que depuis le thread du framework.
/// </remarks>
internal static class DrawReadiness
{
    public static unsafe bool IsReady(IGameObject? character)
    {
        if (character is null || character.Address == nint.Zero)
            return false;

        var gameObject = (GameObject*)character.Address;

        if (gameObject->DrawObject == null)
            return false;

        if ((int)gameObject->RenderFlags != 0)
            return false;

        if (gameObject->DrawObject->Object.GetObjectType() is not SceneObjectType.CharacterBase)
            return true;

        var model = (CharacterBase*)gameObject->DrawObject;

        return model->HasModelInSlotLoaded == 0 && model->HasModelFilesInSlotLoaded == 0;
    }
}
