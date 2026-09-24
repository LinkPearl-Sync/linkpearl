using System.Security.Cryptography;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Identity;

namespace Linkpearl.Core.Groups;

/// <summary>
/// Ce qui fait d'une politique décodée une politique acceptable.
/// </summary>
/// <remarks>
/// Tout se vérifie à partir de la politique elle-même et de la clé du groupe :
/// un membre qui vient d'entrer n'a aucun historique sur lequel s'appuyer.
/// </remarks>
public static class GroupPolicyRules
{
    public static bool TryAccept(
        ReadOnlySpan<byte> encoded, GroupId expected, ReadOnlySpan<byte> groupKey,
        out GroupPolicy? policy, out string? rejection)
    {
        policy = null;

        if (GroupPolicyCodec.TryDecode(encoded, out var decoded, out rejection) is false)
            return false;

        var candidate = decoded!;

        // Avant toute vérification de signature : une clé de groupe qui ne
        // produit pas l'identifiant attendu n'a aucune raison qu'on lui prête
        // la moindre autorité sur cette politique.
        if (GroupId.Of(groupKey) != expected)
        {
            rejection = "clé de groupe qui ne donne pas cet identifiant";
            return false;
        }

        if (candidate.Group != expected || candidate.Attestation.Group != expected)
        {
            rejection = "politique d'un autre groupe";
            return false;
        }

        if (candidate.Attestation.Admission == AdmissionMode.Password && candidate.Password.Length == 0)
        {
            rejection = "mode mot de passe sans mot de passe";
            return false;
        }

        try
        {
            using (var group = CryptoPrimitives.ImportVerifier(CryptoPrimitives.Decompress(groupKey)))
            {
                if (CryptoPrimitives.Verify(group, GroupPolicyCodec.AttestationSignedPortion(candidate.Attestation),
                        candidate.Attestation.Signature) is false)
                {
                    rejection = "attestation que le groupe n'a pas signée";
                    return false;
                }
            }

            var byOwner = candidate.Signer.AsSpan().SequenceEqual(groupKey);

            if (byOwner is false && candidate.IsModerator(candidate.Signer) is false)
            {
                rejection = "signataire ni propriétaire ni modérateur";
                return false;
            }

            using (var signer = CryptoPrimitives.ImportVerifier(CryptoPrimitives.Decompress(candidate.Signer)))
            {
                if (CryptoPrimitives.Verify(signer, GroupPolicyCodec.SignedPortion(candidate), candidate.Signature) is false)
                {
                    rejection = "signature de politique invalide";
                    return false;
                }
            }

            // Le propriétaire-membre et chaque modérateur attesté sont
            // protégés d'un bannissement par clé, quel que soit le
            // signataire : pour écarter un modérateur, il faut d'abord le
            // retirer des modérateurs (tâche 4), pas le bannir sous
            // l'attestation qui l'y nomme encore.
            //
            // GroupPolicy.ProtectedPeers ignore sans lever une clé qui ne se
            // décompresse pas, pour rester utilisable sur une politique
            // seulement décodée. Ici la politique va être acceptée : on
            // décompresse d'abord explicitement Owner et chaque modérateur,
            // pour qu'une clé malformée fasse échouer l'acceptation comme
            // avant, via le catch ci-dessous, plutôt que d'être ignorée en
            // silence par ProtectedPeers.
            CryptoPrimitives.Decompress(candidate.Attestation.Owner);

            foreach (var moderator in candidate.Attestation.Moderators)
                CryptoPrimitives.Decompress(moderator);

            var protectedPeers = candidate.ProtectedPeers();

            if (candidate.Bans.Any(ban => ban.Peer is { } peer && protectedPeers.Contains(peer)))
            {
                rejection = "politique qui bannit le propriétaire ou un modérateur";
                return false;
            }

            // Ce qu'un modérateur ne peut pas faire, même par une politique
            // bien signée : dissoudre.
            if (byOwner is false && candidate.Dissolved)
            {
                rejection = "seul le propriétaire dissout";
                return false;
            }
        }
        catch (CryptographicException e)
        {
            rejection = $"clé de politique invalide : {e.Message}";
            return false;
        }

        policy = candidate;
        rejection = null;
        return true;
    }

    /// <summary>
    /// Vrai si la candidate doit remplacer la politique courante.
    /// </summary>
    /// <remarks>
    /// La dissolution d'abord, et absorbante dans les deux sens : un
    /// modérateur retiré garde une attestation qui le nomme encore, et
    /// pourrait sinon ressusciter un groupe dissous en publiant une version
    /// plus haute non dissoute sous elle. L'attestation ensuite : un
    /// modérateur retiré garde l'ancienne attestation qui le nomme, et
    /// pourrait sinon publier une version plus haute sous elle. À version
    /// égale, le plus petit condensé du <em>contenu</em> signé l'emporte, et
    /// non de la signature : ECDSA est malléable (s ↔ n−s) et aléatoire à
    /// chaque signature, donc deux signatures du même contenu ne doivent
    /// jamais départager. À contenu identique, aucune ne l'emporte.
    /// </remarks>
    public static bool IsNewer(GroupPolicy candidate, GroupPolicy? current)
    {
        if (current is null)
            return true;

        if (candidate.Dissolved != current.Dissolved)
            return candidate.Dissolved;

        if (candidate.Attestation.Version != current.Attestation.Version)
            return candidate.Attestation.Version > current.Attestation.Version;

        if (candidate.Version != current.Version)
            return candidate.Version > current.Version;

        var candidateDigest = SHA256.HashData(GroupPolicyCodec.SignedPortion(candidate));
        var currentDigest = SHA256.HashData(GroupPolicyCodec.SignedPortion(current));

        if (candidateDigest.AsSpan().SequenceEqual(currentDigest))
            return false;

        return candidateDigest.AsSpan().SequenceCompareTo(currentDigest) < 0;
    }
}

/// <summary>Signer une attestation ou une politique.</summary>
public static class GroupPolicySigning
{
    public static byte[] CompressedKey(ECDsa key) => CryptoPrimitives.Compress(CryptoPrimitives.ExportPublicPoint(key));

    public static GroupAttestation SignAttestation(GroupAttestation unsigned, ECDsa groupKey)
        => unsigned with { Signature = CryptoPrimitives.Sign(groupKey, GroupPolicyCodec.AttestationSignedPortion(unsigned)) };

    public static GroupPolicy Sign(GroupPolicy unsigned, ECDsa signer)
    {
        var withSigner = unsigned with { Signer = CompressedKey(signer), Signature = [] };
        return withSigner with { Signature = CryptoPrimitives.Sign(signer, GroupPolicyCodec.SignedPortion(withSigner)) };
    }
}
