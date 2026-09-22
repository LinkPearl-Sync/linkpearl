using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Identity;

/// <summary>
/// La forme collable d'un ticket d'invitation : douze caractères, puis le lieu
/// où le retirer.
/// </summary>
/// <remarks>
/// Le ticket lui-même ne peut pas porter l'adresse : soixante bits sont déjà
/// tout ce qu'il contient. Le suffixe est donc ajouté au texte, exactement
/// comme le code de pairage long le fait déjà.
///
/// Celui qui colle voit où le ticket pointe avant de le remettre, et c'est là
/// qu'il décide s'il fait confiance à ce service. Le cacher reviendrait à lui
/// faire ouvrir une connexion vers un inconnu sans le lui dire.
/// </remarks>
public static class InvitationTicketText
{
    public static string Encode(InvitationTicket ticket, RendezvousAddress at)
        => $"{ticket.Encode()}@{at}";

    /// <summary>
    /// Lit un ticket collé.
    /// </summary>
    /// <remarks>
    /// Le lieu vaut null quand le texte n'en porte pas, à charge de l'appelant
    /// de retomber sur son premier service : c'est ce que désignaient
    /// implicitement les tickets d'avant la fédération.
    /// </remarks>
    public static bool TryParse(
        string? text, out InvitationTicket ticket, out RendezvousAddress? at, out string? rejection)
    {
        ticket = default;
        at = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            rejection = "ticket vide";
            return false;
        }

        var cleaned = text.Trim();
        var separator = cleaned.IndexOf('@');

        if (separator >= 0)
        {
            // Refus en entier si le lieu est illisible : retirer le ticket
            // ailleurs enverrait la demande à quelqu'un d'autre.
            if (RendezvousAddress.TryParse(cleaned[(separator + 1)..], out var parsed, out var why) is false)
            {
                rejection = $"lieu de retrait illisible : {why}";
                return false;
            }

            at = parsed;
            cleaned = cleaned[..separator];
        }

        return InvitationTicket.TryParse(cleaned, out ticket, out rejection);
    }
}
