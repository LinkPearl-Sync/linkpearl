using System.Security.Cryptography;
using System.Text.Json;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Identity;

namespace Linkpearl.Integration;

/// <summary>
/// Conserve le carnet de pairs, protégé par DPAPI.
/// </summary>
/// <remarks>
/// Protégé parce qu'il contient les secrets de paire, dont sont dérivés les
/// jetons de rendez-vous. Quelqu'un qui les lirait pourrait observer les
/// présences de ces paires sur le service.
///
/// Un format de transfert explicite plutôt que la sérialisation directe des
/// types du noyau : ceux-ci vont changer, et un carnet illisible après une mise
/// à jour ferait perdre tous les pairages.
/// </remarks>
public sealed class PairBookStore(string path)
{
    private static readonly byte[] Entropy = "linkpearl:pairs:v1"u8.ToArray();

    private sealed record Dto(
        string Id, string? PublicKey, string PairSecret, string DisplayName, string RendezvousHost,
        int Trust, int Permissions, int Policy, bool Paused,
        long PairedAt, long? LastSeenAt, string? PinnedFingerprint);

    public void Load(PairBook book)
    {
        try
        {
            if (File.Exists(path) is false)
                return;

            var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            var records = JsonSerializer.Deserialize<List<Dto>>(plain) ?? [];

            book.Load(records.Select(Rehydrate).OfType<PairRecord>());
        }
        catch (Exception e) when (e is CryptographicException or IOException or JsonException)
        {
            // Carnet illisible : on démarre avec un carnet vide plutôt que
            // d'empêcher le plugin de se charger. Rien n'est écrasé tant que
            // l'utilisateur n'ajoute pas un pair.
        }
    }

    public void Save(PairBook book)
    {
        var dtos = book.All.Select(record => new Dto(
            Convert.ToHexStringLower(record.Id.ToBytes()),
            record.PublicKey is { } key ? Convert.ToHexStringLower(key) : null,
            Convert.ToHexStringLower(record.PairSecret),
            record.DisplayName,
            record.RendezvousHost,
            (int)record.Trust,
            (int)record.Permissions,
            (int)record.Policy,
            record.Paused,
            record.PairedAt.ToUnixTimeSeconds(),
            record.LastSeenAt?.ToUnixTimeSeconds(),
            record.PinnedFingerprint is { } print ? Convert.ToHexStringLower(print.ToBytes()) : null));

        var plain = JsonSerializer.SerializeToUtf8Bytes(dtos.ToList());

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".part";
        File.WriteAllBytes(temporary, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
        File.Move(temporary, path, overwrite: true);
    }

    private static PairRecord? Rehydrate(Dto dto)
    {
        try
        {
            return new PairRecord
            {
                Id = PeerId.FromBytes(Convert.FromHexString(dto.Id)),
                PublicKey = dto.PublicKey is { } key ? Convert.FromHexString(key) : null,
                PairSecret = Convert.FromHexString(dto.PairSecret),
                DisplayName = dto.DisplayName,
                RendezvousHost = dto.RendezvousHost,
                Trust = (PairTrust)dto.Trust,
                Permissions = (PairPermissions)dto.Permissions,
                Policy = (ConnectionPolicy)dto.Policy,
                Paused = dto.Paused,
                PairedAt = DateTimeOffset.FromUnixTimeSeconds(dto.PairedAt),
                LastSeenAt = dto.LastSeenAt is { } seen ? DateTimeOffset.FromUnixTimeSeconds(seen) : null,
                PinnedFingerprint = dto.PinnedFingerprint is { } print
                    ? PlayerFingerprint.FromBytes(Convert.FromHexString(print))
                    : null,
            };
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            return null;   // une entrée abîmée ne doit pas emporter tout le carnet
        }
    }
}
