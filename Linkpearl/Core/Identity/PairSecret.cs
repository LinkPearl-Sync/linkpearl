using System.Security.Cryptography;

namespace Linkpearl.Core.Identity;

/// <summary>
/// Le secret partagé d'une paire, dérivé sans échange.
/// </summary>
/// <remarks>
/// Il ne sert jamais à chiffrer une session : le canal de données tire ses clés
/// d'un accord éphémère, pour la confidentialité persistante. Celui-ci ne sert
/// qu'à dériver les jetons de rendez-vous et à sceller le bloc de candidats,
/// c'est-à-dire à cacher au serveur qui parle à qui.
///
/// Les deux clés publiques entrent dans le sel <b>triées</b> : le secret ne doit
/// pas dépendre de qui a invité l'autre, sans quoi les deux pairs
/// s'annonceraient sous des jetons différents et ne se trouveraient jamais.
/// </remarks>
public static class PairSecret
{
    public const int SizeInBytes = 32;

    private static ReadOnlySpan<byte> Info => "linkpearl:pair:v1"u8;

    public static byte[] Derive(ReadOnlySpan<byte> pairingNonce, byte[] oneKey, byte[] otherKey)
    {
        var lower = Compare(oneKey, otherKey) <= 0 ? oneKey : otherKey;
        var upper = ReferenceEquals(lower, oneKey) ? otherKey : oneKey;

        var salt = new byte[lower.Length + upper.Length];
        lower.CopyTo(salt, 0);
        upper.CopyTo(salt, lower.Length);

        var prk = new byte[SizeInBytes];
        HKDF.Extract(HashAlgorithmName.SHA256, pairingNonce, salt, prk);

        var secret = new byte[SizeInBytes];
        HKDF.Expand(HashAlgorithmName.SHA256, prk, secret, Info);
        return secret;
    }

    private static int Compare(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b) => a.SequenceCompareTo(b);
}
