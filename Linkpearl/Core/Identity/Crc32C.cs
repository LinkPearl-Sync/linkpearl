namespace Linkpearl.Core.Identity;

/// <summary>
/// Somme de contrôle CRC-32C, dite de Castagnoli.
/// </summary>
/// <remarks>
/// Écrite ici plutôt que tirée d'un paquet : quinze lignes et une table valent
/// mieux qu'une dépendance pour le noyau. Elle sert uniquement à détecter une
/// faute de frappe dans un code d'invitation, avant toute opération réseau.
/// Ce n'est pas une protection contre une altération volontaire, et elle ne
/// prétend pas l'être : l'authenticité vient de la signature du handshake.
/// </remarks>
public static class Crc32C
{
    private const uint Polynomial = 0x82F63B78;

    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];

        for (var i = 0u; i < 256; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? (value >> 1) ^ Polynomial : value >> 1;

            table[i] = value;
        }

        return table;
    }

    public static uint Of(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var b in data)
            crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];

        return ~crc;
    }
}
