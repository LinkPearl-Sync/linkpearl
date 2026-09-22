namespace Linkpearl.Core.Identity;

/// <summary>
/// Base32 de Crockford.
/// </summary>
/// <remarks>
/// L'alphabet écarte I, L, O et U : les trois premiers se confondent avec 1 et
/// 0, le dernier évite de former des mots malheureux. À la lecture on accepte
/// quand même I et L pour 1 et O pour 0, parce que la personne qui a entendu le
/// code au micro les aura écrits ainsi, et qu'un « code invalide » sans
/// explication n'aide personne.
/// </remarks>
public static class Base32Crockford
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var output = new System.Text.StringBuilder((data.Length * 8 / 5) + 1);
        var buffer = 0;
        var bits = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;

            while (bits >= 5)
            {
                output.Append(Alphabet[(buffer >> (bits - 5)) & 0x1F]);
                bits -= 5;
            }
        }

        if (bits > 0)
            output.Append(Alphabet[(buffer << (5 - bits)) & 0x1F]);

        return output.ToString();
    }

    public static bool TryDecode(string text, out byte[] data)
    {
        var output = new List<byte>(text.Length * 5 / 8);
        var buffer = 0;
        var bits = 0;
        data = [];

        foreach (var raw in text)
        {
            if (raw is '-')
                continue;   // séparateur de groupes, purement visuel

            var value = ValueOf(char.ToUpperInvariant(raw));
            if (value < 0)
                return false;

            buffer = (buffer << 5) | value;
            bits += 5;

            if (bits < 8)
                continue;

            output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
            bits -= 8;
        }

        data = output.ToArray();
        return true;
    }

    private static int ValueOf(char c)
        => c switch
        {
            'I' or 'L' => 1,   // entendus pour 1
            'O' => 0,          // entendu pour 0
            _ => Alphabet.IndexOf(c, StringComparison.Ordinal),
        };
}
