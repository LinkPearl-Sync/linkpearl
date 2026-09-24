using System.Security.Cryptography;

namespace Linkpearl.Core.Identity;

/// <summary>
/// Le secret partagé d'une paire, dérivé de l'accord de pairage.
/// </summary>
/// <remarks>
/// Il ne sert jamais à chiffrer une session : le canal de données tire ses clés
/// d'un accord éphémère, pour la confidentialité persistante. Celui-ci ne sert
/// qu'à dériver les jetons de rendez-vous et à sceller le bloc de candidats,
/// c'est-à-dire à cacher au serveur qui parle à qui.
///
/// Le matériau est l'accord des éphémères échangés dans la demande et la
/// réponse de pairage, suivi de l'aléa (voir
/// <see cref="Sync.PairRequestMessage.AgreeOnPairing"/>). En version 1, c'était
/// l'aléa seul, qui voyageait en clair : tout rendez-vous qui voyait passer le
/// pairage connaissait le secret. Le changement d'étiquette empêche qu'un
/// ancien matériau donne jamais le même secret qu'un nouveau.
///
/// Les deux identifiants entrent dans le sel <b>triés</b> : le secret ne doit
/// pas dépendre de qui a invité l'autre, sans quoi les deux pairs
/// s'annonceraient sous des jetons différents et ne se trouveraient jamais.
/// </remarks>
public static class PairSecret
{
    public const int SizeInBytes = 32;

    private static ReadOnlySpan<byte> Info => "linkpearl:pair:v2"u8;

    public static byte[] Derive(ReadOnlySpan<byte> pairingMaterial, PeerId one, PeerId other)
    {
        var a = one.ToBytes();
        var b = other.ToBytes();

        var lower = Compare(a, b) <= 0 ? a : b;
        var upper = ReferenceEquals(lower, a) ? b : a;

        var salt = new byte[lower.Length + upper.Length];
        lower.CopyTo(salt, 0);
        upper.CopyTo(salt, lower.Length);

        var prk = new byte[SizeInBytes];
        HKDF.Extract(HashAlgorithmName.SHA256, pairingMaterial, salt, prk);

        var secret = new byte[SizeInBytes];
        HKDF.Expand(HashAlgorithmName.SHA256, prk, secret, Info);
        return secret;
    }

    private static int Compare(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b) => a.SequenceCompareTo(b);
}
