using System.Security.Cryptography;
using System.Text.Json;

namespace Linkpearl.Integration;

/// <summary>Une invitation déposée, dont on attend encore la réponse.</summary>
public sealed record PendingInvitation(string Ticket, byte[] Nonce, DateTimeOffset CreatedAt);

/// <summary>
/// Conserve les invitations en attente.
/// </summary>
/// <remarks>
/// L'aléa doit survivre à un redémarrage du jeu : sans lui, on ne saurait plus
/// dériver le secret de paire quand la réponse arrivera, et l'invitation serait
/// perdue.
/// </remarks>
public sealed class PendingInvitationStore(string path)
{
    private static readonly byte[] Entropy = "linkpearl:invitations:v1"u8.ToArray();

    private sealed record Dto(string Ticket, string Nonce, long CreatedAt);

    public List<PendingInvitation> Load()
    {
        try
        {
            if (File.Exists(path) is false)
                return [];

            var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);

            return (JsonSerializer.Deserialize<List<Dto>>(plain) ?? [])
                .Select(d => new PendingInvitation(
                    d.Ticket, Convert.FromHexString(d.Nonce), DateTimeOffset.FromUnixTimeSeconds(d.CreatedAt)))
                .ToList();
        }
        catch (Exception e) when (e is CryptographicException or IOException or JsonException or FormatException)
        {
            return [];
        }
    }

    public void Save(IEnumerable<PendingInvitation> invitations)
    {
        var dtos = invitations
            .Select(i => new Dto(i.Ticket, Convert.ToHexStringLower(i.Nonce), i.CreatedAt.ToUnixTimeSeconds()))
            .ToList();

        var plain = JsonSerializer.SerializeToUtf8Bytes(dtos);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".part";
        File.WriteAllBytes(temporary, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
        File.Move(temporary, path, overwrite: true);
    }
}
