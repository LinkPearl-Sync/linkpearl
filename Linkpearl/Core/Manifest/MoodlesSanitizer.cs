using System.Security.Cryptography;
using System.Text;

namespace Linkpearl.Core.Manifest;

/// <summary>
/// Retire des Moodles ce qui désigne des personnes ou relie des personnages.
/// </summary>
/// <remarks>
/// <c>Applier</c> et <c>Dispeller</c> portent « Nom@Monde », parfois celui d'un
/// tiers. Le GUID d'un moodle enregistré est le même sur tous les personnages
/// d'une même personne : transmis tel quel, il permettrait de les relier, ce que
/// l'identité par personnage cherche précisément à empêcher. Il est remplacé
/// par un HMAC sous une clé propre au personnage, stable d'une mise à jour à
/// l'autre. <c>CustomFXPath</c> fait jouer un VFX : ressource transitoire, qui
/// relève du blocage du sous-projet B.
/// </remarks>
public static class MoodlesSanitizer
{
    private static readonly byte[] Info = Encoding.ASCII.GetBytes("linkpearl:moodles-guid:v1");

    public static byte[] KeyFor(ReadOnlySpan<byte> identitySecret)
        => HKDF.DeriveKey(HashAlgorithmName.SHA256, identitySecret.ToArray(), 32, salt: [], info: Info);

    public static string? Sanitize(string base64, ReadOnlySpan<byte> key)
    {
        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }

        if (MoodlesCodec.TryDecode(raw, out var statuses) is false)
            return null;

        var clean = new List<MoodleStatus>(statuses.Count);

        foreach (var s in statuses)
        {
            clean.Add(s with
            {
                Id = Rename(s.Id, key),
                ChainedStatus = s.ChainedStatus == Guid.Empty ? Guid.Empty : Rename(s.ChainedStatus, key),
                CustomFxPath = "",
                Applier = "",
                Dispeller = "",
            });
        }

        return Convert.ToBase64String(MoodlesCodec.Encode(clean));
    }

    public static bool IsSanitizedAndBounded(string base64)
    {
        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return false;
        }

        return MoodlesCodec.TryDecode(raw, out var statuses)
            && statuses.All(s => s.Applier.Length == 0 && s.Dispeller.Length == 0 && s.CustomFxPath.Length == 0);
    }

    private static Guid Rename(Guid original, ReadOnlySpan<byte> key)
    {
        Span<byte> mac = stackalloc byte[32];
        HMACSHA256.HashData(key, original.ToByteArray(), mac);
        return new Guid(mac[..16]);
    }
}
