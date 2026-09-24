using System.Text.Json;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Groups;

/// <summary>
/// La forme sur disque des groupes, sans le chiffrement.
/// </summary>
/// <remarks>
/// Dans le noyau pour se tester sous Linux ; le chiffrement DPAPI reste dans
/// l'adaptateur. Un format explicite plutôt que la sérialisation des types du
/// noyau, pour la même raison que le carnet : ceux-ci vont changer, et des
/// groupes illisibles après une mise à jour seraient perdus. Les champs à venir
/// (politique, clé de signature) s'ajouteront en fin d'enregistrement, nullables.
///
/// Le fichier se relit comme un message venu d'ailleurs, puisqu'il voyagera
/// dans la sauvegarde : une entrée hors des règles est ignorée, pas le
/// fichier entier.
/// </remarks>
public static class GroupBookCodec
{
    private sealed record MemberDto(
        string Fingerprint, string? Id, string DisplayName, long? LastSeenAt, bool Paused, int Receive,
        string? PublicKey = null);

    private sealed record GroupDto(
        string Id, string Name, string Secret, string[] Rendezvous, long JoinedAt, MemberDto[] Members,
        string? OwnerKey = null, string? SigningKey = null, string? Policy = null);

    /// <summary>Même borne que la clé PKCS#8 qu'une identité ECDSA P-256 exporte.</summary>
    private const int MaxSigningKeyLength = 1024;

    private const int ReceiveAnimations = 1;
    private const int ReceiveVfx = 2;
    private const int ReceiveSounds = 4;

    /// <summary>Même borne que le nom de personnage dans une demande de pairage.</summary>
    private const int MaxNameLength = 64;

    public static byte[] Encode(IEnumerable<GroupRecord> groups)
        => JsonSerializer.SerializeToUtf8Bytes(groups.Select(group => new GroupDto(
            Convert.ToHexStringLower(group.Id.ToBytes()),
            group.Name,
            Convert.ToHexStringLower(group.Secret),
            [.. group.Rendezvous.Select(place => place.ToString())],
            group.JoinedAt.ToUnixTimeSeconds(),
            [.. group.Members.Values.Select(member => new MemberDto(
                Convert.ToHexStringLower(member.Fingerprint.ToBytes()),
                member.Id is { } id ? Convert.ToHexStringLower(id.ToBytes()) : null,
                member.DisplayName,
                member.LastSeenAt?.ToUnixTimeSeconds(),
                member.Paused,
                ToBits(member.Receive),
                member.PublicKey is { } publicKey ? Convert.ToHexStringLower(publicKey) : null))],
            group.OwnerKey is { } ownerKey ? Convert.ToHexStringLower(ownerKey) : null,
            group.SigningKey is { } signingKey ? Convert.ToHexStringLower(signingKey) : null,
            group.Policy is { } policy ? Convert.ToHexStringLower(GroupPolicyCodec.Encode(policy)) : null)).ToList());

    public static IReadOnlyList<GroupRecord> Decode(ReadOnlySpan<byte> json)
    {
        var dtos = JsonSerializer.Deserialize<List<GroupDto?>>(json) ?? [];

        return [.. dtos.Select(Rehydrate).OfType<GroupRecord>().Take(GroupBook.MaxGroups)];
    }

    public static bool IsValid(byte[] json)
    {
        try
        {
            _ = Decode(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static GroupRecord? Rehydrate(GroupDto? dto)
    {
        if (dto is null)
            return null;

        try
        {
            var secret = Convert.FromHexString(dto.Secret);

            if (secret.Length != GroupDerivation.SecretSize || dto.Name.Length is 0 or > MaxNameLength)
                return null;

            var places = (dto.Rendezvous ?? [])
                .Select(text => RendezvousAddress.TryParse(text, out var place, out _) ? place : (RendezvousAddress?)null)
                .OfType<RendezvousAddress>()
                .ToList();

            // Un groupe sans service est injoignable, comme un pair sans service.
            if (places.Count == 0)
                return null;

            var members = (dto.Members ?? [])
                .Select(RehydrateMember)
                .OfType<GroupMember>()
                .Take(GroupBook.MaxMembersPerGroup)
                .GroupBy(member => member.Fingerprint)
                .ToDictionary(same => same.Key, same => same.First());

            var id = GroupId.FromBytes(Convert.FromHexString(dto.Id));

            byte[]? ownerKey = null;

            if (dto.OwnerKey is { } ownerKeyHex)
            {
                var candidate = Convert.FromHexString(ownerKeyHex);

                // Une clé qui ne redonne pas l'identifiant du groupe n'a
                // aucune raison qu'on lui prête la moindre autorité : elle
                // ferait accepter n'importe quelle politique prétendument
                // signée par le groupe. L'entrée entière est rejetée plutôt
                // que gardée sans clé, pour ne pas faire disparaître en
                // silence un groupe privé en groupe d'essai.
                if (candidate.Length != CryptoPrimitives.CompressedPointLength || GroupId.Of(candidate) != id)
                    return null;

                ownerKey = candidate;
            }

            byte[]? signingKey = null;

            if (dto.SigningKey is { } signingKeyHex)
            {
                // La clé de signature n'a de sens que pour le propriétaire, et
                // le propriétaire est celui dont la clé redonne l'identifiant :
                // sans OwnerKey, une SigningKey ne prouve rien et rejette
                // l'entrée plutôt que de la garder à moitié.
                if (ownerKey is null)
                    return null;

                var candidate = Convert.FromHexString(signingKeyHex);

                if (candidate.Length > MaxSigningKeyLength)
                    return null;

                signingKey = candidate;
            }

            GroupPolicy? policy = null;

            if (dto.Policy is { Length: > 0 } policyHex && ownerKey is not null)
            {
                var encoded = Convert.FromHexString(policyHex);

                // Une politique qui ne passe plus TryAccept (clé altérée,
                // signature invalide, bornes dépassées) est oubliée : le
                // groupe reste, seule la politique retombe à néant.
                if (GroupPolicyRules.TryAccept(encoded, id, ownerKey, out var accepted, out _))
                    policy = accepted;
            }

            return new GroupRecord
            {
                Id = id,
                Name = dto.Name,
                Secret = secret,
                Rendezvous = places,
                JoinedAt = DateTimeOffset.FromUnixTimeSeconds(dto.JoinedAt),
                Members = members,
                OwnerKey = ownerKey,
                SigningKey = signingKey,
                Policy = policy,
            };
        }
        catch (Exception e) when (e is FormatException or ArgumentException or NullReferenceException)
        {
            return null;   // une entrée abîmée ne doit pas emporter les autres
        }
    }

    private static GroupMember? RehydrateMember(MemberDto? dto)
    {
        if (dto?.DisplayName is not { Length: <= MaxNameLength })
            return null;

        try
        {
            var id = dto.Id is { } idHex ? PeerId.FromBytes(Convert.FromHexString(idHex)) : (PeerId?)null;

            byte[]? publicKey = null;

            if (dto.PublicKey is { } publicKeyHex)
            {
                var candidate = Convert.FromHexString(publicKeyHex);

                // Une clé complète incohérente (mauvaise taille, épinglage
                // absent, ou qui ne redonne pas l'Id épinglé) ne peut pas
                // servir à nommer un modérateur (tâche 4) : elle attesterait
                // une clé qui n'est pas celle du membre. Ce n'est pas une
                // raison de perdre le membre ni son épinglage pour autant :
                // seule la clé retombe à null, et Admit la recomplétera au
                // prochain contact avec la vraie clé.
                if (candidate.Length == CryptoPrimitives.PublicPointLength
                    && id is { } pinned && PeerId.Of(candidate) == pinned)
                {
                    publicKey = candidate;
                }
            }

            return new GroupMember
            {
                Fingerprint = PlayerFingerprint.FromBytes(Convert.FromHexString(dto.Fingerprint)),
                Id = id,
                DisplayName = dto.DisplayName,
                LastSeenAt = dto.LastSeenAt is { } seen ? DateTimeOffset.FromUnixTimeSeconds(seen) : null,
                Paused = dto.Paused,
                Receive = FromBits(dto.Receive),
                PublicKey = publicKey,
            };
        }
        catch (Exception e) when (e is FormatException or ArgumentException or NullReferenceException)
        {
            return null;
        }
    }

    private static int ToBits(TransientCategories receive)
        => (receive.Animations ? ReceiveAnimations : 0)
         | (receive.Vfx ? ReceiveVfx : 0)
         | (receive.Sounds ? ReceiveSounds : 0);

    private static TransientCategories FromBits(int bits)
        => new((bits & ReceiveAnimations) != 0, (bits & ReceiveVfx) != 0, (bits & ReceiveSounds) != 0);
}
