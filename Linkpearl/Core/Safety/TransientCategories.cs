namespace Linkpearl.Core.Safety;

/// <summary>
/// Ce qu'on accepte d'un pair parmi les ressources transitoires : ses
/// animations, ses effets visuels, ses sons.
/// </summary>
/// <remarks>
/// Trois catégories, comme les clients comparables, parce que ce sont les trois
/// choses qu'un joueur veut couper séparément : une danse qui gêne, des
/// particules qui envahissent une place bondée, un son trop fort. Le <c>.tmb</c>
/// va avec les animations : c'est la trame qui les déclenche, même quand elle
/// appelle aussi un effet ou un son.
///
/// Un fichier qui n'est pas transitoire est toujours permis : ces catégories
/// ne bloquent jamais l'apparence statique.
/// </remarks>
public readonly record struct TransientCategories(bool Animations, bool Vfx, bool Sounds)
{
    public static TransientCategories All { get; } = new(true, true, true);

    public static TransientCategories None { get; } = new(false, false, false);

    /// <summary>Ce que les deux permettent : le réglage global et celui d'un pair.</summary>
    public TransientCategories And(TransientCategories other)
        => new(Animations && other.Animations, Vfx && other.Vfx, Sounds && other.Sounds);

    public bool Allows(string gamePath) => ExtensionOf(gamePath) switch
    {
        ".pap" or ".tmb" => Animations,
        ".avfx" or ".atex" => Vfx,
        ".scd" => Sounds,
        _ => true,
    };

    public static bool IsTransient(string gamePath)
        => ExtensionOf(gamePath) is ".pap" or ".tmb" or ".avfx" or ".atex" or ".scd";

    /// <summary>La dernière extension du dernier segment, en minuscules, ou une chaîne vide.</summary>
    private static string ExtensionOf(string gamePath)
    {
        var fileStart = gamePath.LastIndexOf('/') + 1;
        var dot = gamePath.LastIndexOf('.');

        return dot >= fileStart && dot < gamePath.Length - 1
            ? gamePath[dot..].ToLowerInvariant()
            : string.Empty;
    }
}
