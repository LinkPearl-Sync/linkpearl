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

            var owner = PeerId.Of(CryptoPrimitives.Decompress(candidate.Attestation.Owner));

            if (candidate.Bans.Any(ban => ban.Peer == owner))
            {
                rejection = "politique qui bannit le propriétaire";
                return false;
            }

            if (byOwner is false)
            {
                // Ce qu'un modérateur ne peut pas faire, même par une politique
                // bien signée : dissoudre, ou écarter ses pairs.
                if (candidate.Dissolved)
                {
                    rejection = "seul le propriétaire dissout";
                    return false;
                }

                var moderators = candidate.Attestation.Moderators
                    .Select(key => PeerId.Of(CryptoPrimitives.Decompress(key)))
                    .ToHashSet();

                if (candidate.Bans.Any(ban => ban.Peer is { } peer && moderators.Contains(peer)))
                {
                    rejection = "un modérateur ne bannit pas un modérateur";
                    return false;
                }
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
    /// L'attestation d'abord : un modérateur retiré garde l'ancienne attestation
    /// qui le nomme, et pourrait sinon publier une version plus haute sous elle.
    /// À version égale, la plus petite empreinte de signature l'emporte, pour que
    /// deux membres qui voient les deux mêmes politiques choisissent la même.
    /// </remarks>
    public static bool IsNewer(GroupPolicy candidate, GroupPolicy? current)
    {
        if (current is null)
            return true;

        if (candidate.Attestation.Version != current.Attestation.Version)
            return candidate.Attestation.Version > current.Attestation.Version;

        if (candidate.Version != current.Version)
            return candidate.Version > current.Version;

        return SHA256.HashData(candidate.Signature).AsSpan().SequenceCompareTo(SHA256.HashData(current.Signature)) < 0;
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
