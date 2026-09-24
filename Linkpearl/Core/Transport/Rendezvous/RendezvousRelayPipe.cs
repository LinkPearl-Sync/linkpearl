namespace Linkpearl.Core.Transport.Rendezvous;

/// <summary>Le relais d'un rendez-vous, vu comme un tuyau vers le pair.</summary>
/// <remarks>
/// La connexion ne sert plus qu'à cela une fois le relais ouvert : le service
/// la met bout à bout avec celle du pair et ne lit plus rien d'autre dessus.
/// Un seul lecteur et un seul écrivain, ce que le flux supporte.
/// </remarks>
public sealed class RendezvousRelayPipe(RendezvousClient client) : IRelayPipe
{
    public Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken ct) => client.SendRelayAsync(payload, ct);

    public Task<byte[]?> ReceiveAsync(CancellationToken ct) => client.ReceiveRelayAsync(ct);

    public ValueTask DisposeAsync() => client.DisposeAsync();
}
