using Linkpearl.Core.Manifest;

namespace Linkpearl.Core.Sync;

/// <summary>Quels extras ont changé entre deux manifestes.</summary>
public sealed record ExtrasChange(bool CustomizePlus, bool Heels, bool Honorific, bool Moodles, bool PetNicknames)
{
    public static ExtrasChange All { get; } = new(true, true, true, true, true);

    public bool Any => CustomizePlus || Heels || Honorific || Moodles || PetNicknames;
}

/// <summary>
/// Décide si un nouveau manifeste peut se poser sans redessin.
/// </summary>
/// <remarks>
/// Un titre Honorific ou un statut Moodles changent souvent, et un redessin
/// fait clignoter le personnage entier. Quand fichiers, métadonnées et état
/// Glamourer sont identiques, seuls les plugins concernés sont appelés.
/// </remarks>
public static class ExtrasDiff
{
    public static bool OnlyExtrasDiffer(CharacterManifest before, CharacterManifest after)
        => ManifestCodec.HashOf(before with { Extras = null, Version = CharacterManifest.CurrentVersion })
        == ManifestCodec.HashOf(after with { Extras = null, Version = CharacterManifest.CurrentVersion });

    public static ExtrasChange Between(CharacterExtras before, CharacterExtras after)
        => new(before.CustomizePlus != after.CustomizePlus,
               before.Heels != after.Heels,
               before.Honorific != after.Honorific,
               before.Moodles != after.Moodles,
               before.PetNicknames != after.PetNicknames);
}
