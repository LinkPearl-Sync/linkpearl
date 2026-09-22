namespace Linkpearl.Core.Crypto;

/// <summary>
/// Chaîne d'authentification courte, à comparer de vive voix.
/// </summary>
/// <remarks>
/// Le code d'invitation porte déjà la clé publique complète, donc le rendez-vous
/// ne peut pas substituer d'identité : cette chaîne est une vérification
/// supplémentaire, pas la seule barrière. Elle sert surtout quand le code a
/// transité par un canal douteux.
///
/// Six mots tirés d'une liste de soixante-quatre, soit trente-six bits. Les mots
/// sont courts, distincts à l'oreille, et sans accent ni homophone dans la
/// liste : ils sont faits pour être dits au micro, pas lus.
/// </remarks>
public static class ShortAuthString
{
    public const int WordCount = 6;

    private static readonly string[] Words =
    [
        "arbre", "banc",   "bleu",   "bois",   "bulle",  "cadre",  "carte",  "chat",
        "ciel",  "cloche", "corde",  "coupe",  "craie",  "cube",   "digue",  "douze",
        "encre", "fer",    "fleur",  "forge",  "frise",  "givre",  "glace",  "grain",
        "herbe", "huile",  "jade",   "lampe",  "lierre", "lune",   "malle",  "marbre",
        "mousse","neige",  "noix",   "nuage",  "ombre",  "onde",   "orage",  "ortie",
        "perle", "pierre", "pluie",  "pomme",  "pont",   "porte",  "prune",  "quai",
        "roche", "rose",   "roue",   "sable",  "salle",  "sel",    "sept",   "sucre",
        "table", "tige",   "toile",  "tour",   "trois",  "vague",  "vent",   "vigne",
    ];

    public static IReadOnlyList<string> Of(ReadOnlySpan<byte> sessionId)
    {
        if (sessionId.Length < WordCount)
            throw new ArgumentException($"il faut au moins {WordCount} octets", nameof(sessionId));

        var words = new string[WordCount];
        for (var i = 0; i < WordCount; i++)
            words[i] = Words[sessionId[i] & 0x3F];   // six bits par mot

        return words;
    }
}
