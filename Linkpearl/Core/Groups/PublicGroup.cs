using System.Security.Cryptography;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Groups;

/// <summary>
/// Le groupe que tout le monde peut activer, sans code ni admission.
/// </summary>
/// <remarks>
/// Son secret est une constante : quiconque l'active voit tout joueur visible
/// qui l'a activé aussi, et le service, qui la connaît, peut en apparier tous
/// les membres. Il ne lit pas davantage le contenu, qui passe dans la session
/// SIGMA-I. Pas de clé de groupe, donc pas de politique : la modération est
/// le blocage local et les listes de bannissement des services.
/// </remarks>
public static class PublicGroup
{
    public const string Name = "Public";

    private static readonly byte[] SecretBytes = SHA256.HashData("linkpearl:public:v1"u8);

    /// <summary>Une copie : un appelant qui écrirait dedans ne doit rien changer aux autres.</summary>
    public static byte[] Secret => [.. SecretBytes];

    public static GroupId Id { get; } = GroupId.Of(SecretBytes);

    public static bool Is(GroupId id) => id == Id;

    /// <summary>
    /// Le Public tel qu'on l'active la première fois.
    /// </summary>
    /// <remarks>
    /// Animations, VFX et sons coupés : ce sont des inconnus, et une danse ou
    /// des particules imposées par un passant sont ce qu'on veut le moins voir
    /// arriver sans l'avoir choisi.
    /// </remarks>
    public static GroupRecord Create(IReadOnlyList<RendezvousAddress> services, DateTimeOffset now) => new()
    {
        Id = Id,
        Name = Name,
        Secret = Secret,
        Rendezvous = services,
        JoinedAt = now,
        DefaultReceive = TransientCategories.None,
    };
}
