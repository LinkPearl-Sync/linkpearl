using System.Buffers.Binary;

namespace Linkpearl.Core.Transport.Rendezvous;

/// <summary>Types de trame du rendez-vous.</summary>
public static class RendezvousKind
{
    public const byte Announce = 0x01;
    public const byte Matched = 0x02;
    public const byte RelayOpen = 0x03;
    public const byte RelayReady = 0x04;
    public const byte RelayData = 0x05;
    public const byte Error = 0x06;
    public const byte Reflect = 0x07;
    public const byte Reflected = 0x08;
}

/// <summary>Ce qu'un client annonce au rendez-vous.</summary>
/// <remarks>
/// Les jetons sont opaques, et le bloc de candidats est scellé sous une clé
/// dérivée du secret de paire : le serveur ne peut lire ni l'un ni l'autre. Il
/// ne voit que des octets et l'adresse d'où ils viennent.
/// </remarks>
public sealed record Announcement(IReadOnlyList<byte[]> Tickets, byte[] SealedCandidates);

/// <summary>
/// Format de trame du rendez-vous, partagé par le client et le serveur.
/// </summary>
/// <remarks>
/// Partagé, et non dupliqué de part et d'autre : deux encodeurs qui divergent
/// donnent un échec silencieux qu'aucun des deux côtés ne peut diagnostiquer.
/// </remarks>
public static class RendezvousWire
{
    public const int MaxFrameLength = 64 * 1024;
    public const int MaxTicketsPerAnnouncement = 8;
    public const int MaxSealedCandidatesLength = 4096;

    public static byte[] Announce(Announcement announcement)
    {
        var body = new List<byte> { RendezvousKind.Announce, (byte)announcement.Tickets.Count };

        foreach (var ticket in announcement.Tickets)
            body.AddRange(ticket);

        body.Add((byte)(announcement.SealedCandidates.Length >> 8));
        body.Add((byte)announcement.SealedCandidates.Length);
        body.AddRange(announcement.SealedCandidates);

        return body.ToArray();
    }

    public static bool TryReadAnnounce(ReadOnlySpan<byte> frame, out Announcement? announcement, out string? rejection)
    {
        announcement = null;

        if (frame.Length < 2 || frame[0] != RendezvousKind.Announce)
        {
            rejection = "annonce malformée";
            return false;
        }

        var count = frame[1];

        if (count is 0 || count > MaxTicketsPerAnnouncement)
        {
            rejection = $"nombre de jetons hors bornes ({count}, plafond {MaxTicketsPerAnnouncement})";
            return false;
        }

        var offset = 2 + (count * RendezvousTicket.SizeInBytes);

        if (frame.Length < offset + 2)
        {
            rejection = "annonce tronquée";
            return false;
        }

        var tickets = new List<byte[]>(count);
        for (var i = 0; i < count; i++)
            tickets.Add(frame.Slice(2 + (i * RendezvousTicket.SizeInBytes), RendezvousTicket.SizeInBytes).ToArray());

        var sealedLength = (frame[offset] << 8) | frame[offset + 1];
        offset += 2;

        if (sealedLength > MaxSealedCandidatesLength || frame.Length < offset + sealedLength)
        {
            rejection = "bloc de candidats hors bornes ou tronqué";
            return false;
        }

        announcement = new Announcement(tickets, frame.Slice(offset, sealedLength).ToArray());
        rejection = null;
        return true;
    }

    public static byte[] Matched(ReadOnlySpan<byte> sealedCandidates)
    {
        var frame = new byte[1 + sealedCandidates.Length];
        frame[0] = RendezvousKind.Matched;
        sealedCandidates.CopyTo(frame.AsSpan(1));
        return frame;
    }

    public static byte[] RelayOpen(ReadOnlySpan<byte> ticket)
    {
        var frame = new byte[1 + RendezvousTicket.SizeInBytes];
        frame[0] = RendezvousKind.RelayOpen;
        ticket[..RendezvousTicket.SizeInBytes].CopyTo(frame.AsSpan(1));
        return frame;
    }

    public static byte[] Simple(byte kind) => [kind];

    public static byte[] RelayData(ReadOnlySpan<byte> payload)
    {
        var frame = new byte[1 + payload.Length];
        frame[0] = RendezvousKind.RelayData;
        payload.CopyTo(frame.AsSpan(1));
        return frame;
    }

    public static byte[] Error(string reason)
    {
        var text = System.Text.Encoding.UTF8.GetBytes(reason);
        var frame = new byte[1 + text.Length];
        frame[0] = RendezvousKind.Error;
        text.CopyTo(frame.AsSpan(1));
        return frame;
    }

    /// <summary>Préfixe de longueur, pour délimiter les trames sur un flux.</summary>
    public static byte[] Frame(ReadOnlySpan<byte> body)
    {
        var framed = new byte[sizeof(int) + body.Length];
        BinaryPrimitives.WriteInt32BigEndian(framed, body.Length);
        body.CopyTo(framed.AsSpan(sizeof(int)));
        return framed;
    }
}
