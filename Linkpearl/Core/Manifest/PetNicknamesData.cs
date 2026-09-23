using System.Text;

namespace Linkpearl.Core.Manifest;

/// <summary>
/// Les données de PetNicknames, privées de ce qui désigne le joueur.
/// </summary>
/// <remarks>
/// Relevé dans FFXIVPetRenamer 7192264 (WriterElementVersion4) : base64 d'un
/// texte UTF-16 en lignes, dont les lignes 1 à 3 sont le nom du personnage,
/// son monde d'origine et son ContentId. Le projet s'interdit de faire sortir
/// ce dernier : même le dossier local du personnage n'en porte qu'une
/// empreinte. L'émetteur les remplace par des valeurs neutres, et l'adaptateur
/// du receveur y remet celles du personnage qu'il voit, lues chez lui.
/// </remarks>
public static class PetNicknamesData
{
    public const string Header = "[PetNicknames(4)]";
    public const string NeutralName = "Linkpearl";
    public const string NeutralWorld = "0";
    public const string NeutralContentId = "0";
    public const int MaxLines = 64;
    public const int MaxLineLength = 256;

    public static string? Neutralize(string base64)
    {
        if (TrySplit(base64, out var lines, out var separator) is false)
            return null;

        lines[1] = NeutralName;
        lines[2] = NeutralWorld;
        lines[3] = NeutralContentId;

        return Join(lines, separator);
    }

    public static bool IsNeutralAndBounded(string base64)
        => TrySplit(base64, out var lines, out _)
        && lines[1] == NeutralName
        && lines[2] == NeutralWorld
        && lines[3] == NeutralContentId;

    /// <summary>Découpe en lignes, dans les bornes, en-tête vérifié. Jamais d'exception.</summary>
    public static bool TrySplit(string base64, out string[] lines, out string separator)
    {
        lines = [];
        separator = "\r\n";

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return false;
        }

        if (bytes.Length == 0 || bytes.Length % 2 != 0)
            return false;

        var text = Encoding.Unicode.GetString(bytes);
        separator = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var split = text.Split(separator);

        if (split.Length < 4 || split.Length > MaxLines || split[0] != Header)
            return false;

        if (split.Any(line => line.Length > MaxLineLength))
            return false;

        lines = split;
        return true;
    }

    public static string Join(string[] lines, string separator)
        => Convert.ToBase64String(Encoding.Unicode.GetBytes(string.Join(separator, lines)));
}
