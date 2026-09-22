using System.Text;

namespace Linkpearl.Ui;

/// <summary>
/// Rend affichable un texte saisi par un joueur.
/// </summary>
/// <remarks>
/// Les noms donnés aux pairs sont libres, et les joueurs emploient volontiers
/// des caractères décoratifs qu'aucune police de texte ne couvre : pleine
/// chasse, alphabet mathématique, émojis. Sans traitement, ils s'affichent en
/// points d'interrogation, et l'utilisateur croit à un bug alors qu'il a lui-même
/// tapé le nom.
///
/// La normalisation de compatibilité Unicode (NFKC) ramène les deux premières
/// familles à leur équivalent latin. Les émojis, eux, sont retirés : il n'existe
/// pas de repli raisonnable pour eux dans une police vectorielle monochrome.
/// </remarks>
internal static class Glyphs
{
    private static readonly Dictionary<string, string> Cache = [];

    /// <summary>Au-delà, le cache est vidé : il n'évite qu'un recalcul par image.</summary>
    private const int CacheLimit = 256;

    /// <summary>
    /// Symboles absents d'Inter mais assez courants pour mériter un équivalent
    /// plutôt qu'une suppression.
    /// </summary>
    /// <remarks>
    /// Les joueurs substituent volontiers une lettre par un symbole qui lui
    /// ressemble. « ＦＡＣＴ⚙ＲＹ » attend un O, pas un trou.
    /// </remarks>
    private static readonly Dictionary<char, string> Substitutes = new()
    {
        ['⚙'] = "O",
        ['☼'] = "O",
        ['✿'] = "o",
        ['❀'] = "o",
        ['★'] = "*",
        ['☆'] = "*",
        ['✦'] = "◆",
        ['✧'] = "◇",
        ['➡'] = "→",
        ['✨'] = "",
        ['☠'] = "",
        ['❤'] = "",
        ['　'] = " ",
    };

    /// <summary>
    /// Rend un texte affichable. Le résultat est mémorisé : la méthode est
    /// appelée à chaque image, pour chaque ligne de liste.
    /// </summary>
    public static string Safe(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        if (Cache.TryGetValue(text, out var cached))
            return cached;

        var result = Convert(text);

        if (Cache.Count >= CacheLimit)
            Cache.Clear();

        Cache[text] = result;
        return result;
    }

    private static string Convert(string text)
    {
        var builder = new StringBuilder(text.Length);

        // NFKC d'abord : il ramène la pleine chasse et les alphabets
        // mathématiques au latin, ce qui règle la majorité des cas d'un coup.
        foreach (var ch in text.Normalize(NormalizationForm.FormKC))
        {
            if (Substitutes.TryGetValue(ch, out var replacement))
            {
                builder.Append(replacement);
                continue;
            }

            if (IsRenderable(ch))
                builder.Append(ch);
        }

        var result = builder.ToString().Trim();

        // Tout retirer donnerait une ligne vide, où l'utilisateur ne
        // reconnaîtrait plus l'entrée qu'il a créée. Mieux vaut le texte brut.
        return result.Length == 0 ? text.Trim() : result;
    }

    /// <summary>
    /// Vrai pour ce qu'Inter et FontAwesome savent dessiner.
    /// </summary>
    /// <remarks>
    /// Les substituts de paire (U+D800 à U+DFFF) sont écartés : ils codent les
    /// émojis et les alphabets décoratifs hors du plan de base, et la moitié
    /// d'une paire n'a aucun sens isolée.
    /// </remarks>
    private static bool IsRenderable(char ch)
        => char.IsSurrogate(ch) is false
        && (char.IsLetterOrDigit(ch)
         || char.IsPunctuation(ch)
         || char.IsSymbol(ch) is false && char.IsWhiteSpace(ch)
         || ch < 0x2000);
}
