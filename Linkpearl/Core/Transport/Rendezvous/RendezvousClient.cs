using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Linkpearl.Core.Transport.Rendezvous;

/// <summary>
/// Client du service de rendez-vous.
/// </summary>
/// <remarks>
/// Le rendez-vous n'est pas une autorité : tout ce qu'il rend est soit opaque,
/// soit vérifié ailleurs. Un serveur malveillant peut refuser son service ou
/// mentir sur une adresse, ce qui produit un échec de handshake, jamais une
/// usurpation : l'autorisation vient du carnet local et la clé publique du code
/// d'invitation.
/// </remarks>
public sealed class RendezvousClient : IAsyncDisposable
{
    private readonly TcpClient _tcp = new();
    private NetworkStream? _stream;

    public async Task ConnectAsync(string host, int port, CancellationToken ct)
    {
        await _tcp.ConnectAsync(host, port, ct).ConfigureAwait(false);
        _stream = _tcp.GetStream();
    }

    /// <summary>Annonce ses jetons et attend qu'un pair présente le même.</summary>
    public async Task<byte[]?> AnnounceAndWaitAsync(Announcement announcement, CancellationToken ct)
    {
        await SendAsync(RendezvousWire.Announce(announcement), ct).ConfigureAwait(false);

        while (true)
        {
            var frame = await ReadFrameAsync(ct).ConfigureAwait(false);

            if (frame is null)
                return null;

            switch (frame[0])
            {
                case RendezvousKind.Matched:
                    return frame[1..];

                case RendezvousKind.Error:
                    throw new InvalidOperationException(
                        $"rendez-vous : {System.Text.Encoding.UTF8.GetString(frame, 1, frame.Length - 1)}");
            }
        }
    }

    /// <summary>
    /// Ouvre ses boîtes et reste connecté pour recevoir les demandes.
    /// </summary>
    /// <remarks>
    /// La connexion tient lieu de présence : la fermer vaut déclaration
    /// d'absence, sans battement de cœur ni délai d'expiration à régler.
    /// </remarks>
    public Task OpenMailboxesAsync(IReadOnlyList<byte[]> addresses, CancellationToken ct)
        => SendAsync(RendezvousWire.MailboxOpen(addresses), ct);

    /// <summary>Demande lesquelles de ces adresses sont présentes.</summary>
    public async Task<bool[]?> QueryPresenceAsync(IReadOnlyList<byte[]> addresses, CancellationToken ct)
    {
        await SendAsync(RendezvousWire.MailboxQuery(addresses), ct).ConfigureAwait(false);

        while (true)
        {
            var frame = await ReadFrameAsync(ct).ConfigureAwait(false);

            if (frame is null)
                return null;

            switch (frame[0])
            {
                case RendezvousKind.MailboxPresence:
                    return RendezvousWire.TryReadPresence(frame, out var present) ? present : null;

                case RendezvousKind.MailboxDelivery:
                    // Une demande arrivée pendant l'attente : on la met de côté
                    // plutôt que de la perdre, elle n'a aucune raison d'attendre
                    // qu'on ait fini d'interroger.
                    Delivered?.Invoke(frame[1..]);
                    break;

                case RendezvousKind.Error:
                    return null;
            }
        }
    }

    /// <summary>
    /// Demande à ce service la liste de ceux qu'il connaît.
    /// </summary>
    /// <remarks>
    /// Il publie ce que son opérateur a écrit, rien de plus : il n'interroge
    /// aucun autre service et ne relaie rien. Ce qui revient est une
    /// proposition, jamais un ajout : c'est l'utilisateur qui coche.
    /// </remarks>
    public async Task<IReadOnlyList<DirectoryEntry>?> QueryDirectoryAsync(CancellationToken ct)
    {
        await SendAsync(RendezvousWire.Simple(RendezvousKind.DirectoryQuery), ct).ConfigureAwait(false);

        while (true)
        {
            var frame = await ReadFrameAsync(ct).ConfigureAwait(false);

            if (frame is null)
                return null;

            switch (frame[0])
            {
                case RendezvousKind.DirectoryList:
                    return RendezvousWire.TryReadDirectory(frame, out var entries, out _) ? entries : null;

                case RendezvousKind.MailboxDelivery:
                    // Une demande arrivée pendant l'attente : on la met de côté
                    // plutôt que de la perdre.
                    Delivered?.Invoke(frame[1..]);
                    break;

                case RendezvousKind.Error:
                    return null;
            }
        }
    }

    /// <summary>Dépose une demande dans la boîte de quelqu'un.</summary>
    public Task DepositAsync(ReadOnlyMemory<byte> address, ReadOnlyMemory<byte> payload, CancellationToken ct)
        => SendAsync(RendezvousWire.MailboxDeposit(address.Span, payload.Span), ct);

    /// <summary>Appelé quand une demande nous est poussée.</summary>
    public event Action<byte[]>? Delivered;

    /// <summary>
    /// Boucle de réception des remises.
    /// </summary>
    /// <remarks>
    /// À ne lancer que sur une connexion dédiée à la présence : deux lecteurs
    /// concurrents sur un même flux se volent les trames, ce qui s'est déjà vu
    /// sur le relais.
    /// </remarks>
    public async Task ListenAsync(CancellationToken ct)
    {
        while (ct.IsCancellationRequested is false)
        {
            var frame = await ReadFrameAsync(ct).ConfigureAwait(false);

            if (frame is null)
                return;

            if (frame[0] == RendezvousKind.MailboxDelivery)
                Delivered?.Invoke(frame[1..]);
        }
    }

    /// <summary>Dépose une invitation, que le rendez-vous rendra une seule fois.</summary>
    public async Task<string?> RegisterInvitationAsync(
        ReadOnlyMemory<byte> ticket, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        await SendAsync(RendezvousWire.TicketRegister(ticket.Span, payload.Span), ct).ConfigureAwait(false);

        var frame = await ReadFrameAsync(ct).ConfigureAwait(false);

        return frame switch
        {
            null => "le rendez-vous n'a pas répondu",
            [RendezvousKind.TicketAccepted, ..] => null,
            [RendezvousKind.Error, .. var reason] => System.Text.Encoding.UTF8.GetString(reason),
            _ => "réponse inattendue du rendez-vous",
        };
    }

    /// <summary>Retire une invitation. Elle disparaît du serveur au premier retrait.</summary>
    public async Task<(byte[]? Payload, string? Rejection)> RedeemInvitationAsync(
        ReadOnlyMemory<byte> ticket, CancellationToken ct)
    {
        await SendAsync(RendezvousWire.TicketRedeem(ticket.Span), ct).ConfigureAwait(false);

        var frame = await ReadFrameAsync(ct).ConfigureAwait(false);

        return frame switch
        {
            null => (null, "le rendez-vous n'a pas répondu"),
            [RendezvousKind.TicketPayload, .. var payload] => (payload, null),
            [RendezvousKind.Error, .. var reason] => (null, System.Text.Encoding.UTF8.GetString(reason)),
            _ => (null, "réponse inattendue du rendez-vous"),
        };
    }

    /// <summary>Demande un relais et attend que le pair en fasse autant.</summary>
    public async Task<bool> OpenRelayAsync(ReadOnlyMemory<byte> ticket, CancellationToken ct)
    {
        await SendAsync(RendezvousWire.RelayOpen(ticket.Span), ct).ConfigureAwait(false);

        var frame = await ReadFrameAsync(ct).ConfigureAwait(false);
        return frame is not null && frame[0] == RendezvousKind.RelayReady;
    }

    public Task SendRelayAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
        => SendAsync(RendezvousWire.RelayData(payload.Span), ct);

    public async Task<byte[]?> ReceiveRelayAsync(CancellationToken ct)
    {
        var frame = await ReadFrameAsync(ct).ConfigureAwait(false);
        return frame is not null && frame[0] == RendezvousKind.RelayData ? frame[1..] : null;
    }

    /// <summary>
    /// Demande au serveur l'adresse d'où il nous voit.
    /// </summary>
    /// <remarks>
    /// La socket UDP est fournie par l'appelant, et c'est essentiel : c'est
    /// celle qui servira ensuite au trou de NAT, donc c'est son adresse
    /// publique que l'on veut connaître, pas celle d'une autre.
    /// </remarks>
    public static async Task<IPEndPoint?> ReflectAsync(
        Socket socket, EndPoint server, TimeSpan timeout, CancellationToken ct)
    {
        await socket.SendToAsync(new byte[] { RendezvousKind.Reflect }, server, ct).ConfigureAwait(false);

        var buffer = new byte[64];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);

        try
        {
            var received = await socket.ReceiveFromAsync(buffer, server, deadline.Token).ConfigureAwait(false);

            if (received.ReceivedBytes < 4 || buffer[0] != RendezvousKind.Reflected)
                return null;

            var length = buffer[1];

            if (received.ReceivedBytes < 2 + length + 2)
                return null;

            var address = new IPAddress(buffer.AsSpan(2, length));
            var port = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(2 + length));
            return new IPEndPoint(address, port);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private async Task SendAsync(byte[] body, CancellationToken ct)
        => await _stream!.WriteAsync(RendezvousWire.Frame(body), ct).ConfigureAwait(false);

    private async Task<byte[]?> ReadFrameAsync(CancellationToken ct)
    {
        var header = new byte[sizeof(int)];

        if (await ReadExactlyAsync(header, ct).ConfigureAwait(false) is false)
            return null;

        var length = BinaryPrimitives.ReadInt32BigEndian(header);

        // Une longueur venant du réseau ne s'alloue jamais telle quelle.
        if (length is <= 0 or > RendezvousWire.MaxFrameLength)
            return null;

        var body = new byte[length];
        return await ReadExactlyAsync(body, ct).ConfigureAwait(false) ? body : null;
    }

    private async Task<bool> ReadExactlyAsync(Memory<byte> buffer, CancellationToken ct)
    {
        var offset = 0;

        while (offset < buffer.Length)
        {
            var read = await _stream!.ReadAsync(buffer[offset..], ct).ConfigureAwait(false);

            if (read == 0)
                return false;

            offset += read;
        }

        return true;
    }

    public ValueTask DisposeAsync()
    {
        _stream?.Dispose();
        _tcp.Dispose();
        return ValueTask.CompletedTask;
    }
}
