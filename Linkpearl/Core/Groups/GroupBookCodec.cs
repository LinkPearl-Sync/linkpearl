using System.Text.Json;
using Linkpearl.Core.Abstractions;
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
        string Fingerprint, string? Id, string DisplayName, long? LastSeenAt, bool Paused, int Receive);

    private sealed record GroupDto(
        string Id, string Name, string Secret, string[] Rendezvous, long JoinedAt, MemberDto[] Members);

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
                ToBits(member.Receive)))])).ToList());

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

            return new GroupRecord
            {
                Id = GroupId.FromBytes(Convert.FromHexString(dto.Id)),
                Name = dto.Name,
                Secret = secret,
                Rendezvous = places,
                JoinedAt = DateTimeOffset.FromUnixTimeSeconds(dto.JoinedAt),
                Members = members,
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
            return new GroupMember
            {
                Fingerprint = PlayerFingerprint.FromBytes(Convert.FromHexString(dto.Fingerprint)),
                Id = dto.Id is { } id ? PeerId.FromBytes(Convert.FromHexString(id)) : null,
                DisplayName = dto.DisplayName,
                LastSeenAt = dto.LastSeenAt is { } seen ? DateTimeOffset.FromUnixTimeSeconds(seen) : null,
                Paused = dto.Paused,
                Receive = FromBits(dto.Receive),
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
