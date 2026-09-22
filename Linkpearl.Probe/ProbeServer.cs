using System.Net;
using System.Net.Sockets;

namespace Linkpearl.Probe;

/// <summary>
/// Sonde côté serveur : réflexion d'adresse sur deux ports, et rendez-vous par code.
/// </summary>
/// <remarks>
/// Deux ports, parce que c'est ce qui distingue un NAT dont le mapping est
/// indépendant de la destination (le punching a des chances) d'un NAT
/// symétrique (il n'en a pratiquement aucune). Si la même socket cliente se
/// présente sous deux ports publics différents selon le port du serveur
/// contacté, le NAT est symétrique.
///
/// Rien ici ne survivra au jalon 5 : c'est un instrument de mesure, pas une
/// brique du produit.
/// </remarks>
public sealed class ProbeServer
{
    private readonly record struct Waiting(IPEndPoint Observed, List<IPEndPoint> Local, DateTime Since);

    private readonly Dictionary<string, Waiting> _waiting = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public void Run(int basePort)
    {
        var primary   = Bind(basePort);
        var secondary = Bind(basePort + 1);

        Console.WriteLine($"Sonde en écoute sur les ports {basePort} et {basePort + 1} (IPv4 et IPv6).");
        Console.WriteLine("Ctrl+C pour arrêter.");

        var second = new Thread(() => Listen(secondary, 1)) { IsBackground = true };
        second.Start();

        Listen(primary, 0);
    }

    private static Socket Bind(int port)
    {
        var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        socket.DualMode = true;   // une seule socket sert IPv4 et IPv6
        socket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
        return socket;
    }

    private void Listen(Socket socket, byte portIndex)
    {
        var buffer = new byte[2048];

        while (true)
        {
            EndPoint from = new IPEndPoint(IPAddress.IPv6Any, 0);
            int read;

            try
            {
                read = socket.ReceiveFrom(buffer, ref from);
            }
            catch (SocketException)
            {
                continue;
            }

            var sender = Normalize((IPEndPoint)from);
            var frame = buffer.AsSpan(0, read);

            switch (Wire.KindOf(frame))
            {
                case Kind.ClassifyRequest:
                    socket.SendTo(Wire.ClassifyReply(portIndex, sender), from);
                    break;

                case Kind.Register when portIndex == 0:
                    HandleRegister(socket, frame, sender, from);
                    break;
            }
        }
    }

    private void HandleRegister(Socket socket, ReadOnlySpan<byte> frame, IPEndPoint sender, EndPoint raw)
    {
        var (code, local) = Wire.ReadRegister(frame);
        Waiting? partner = null;

        lock (_lock)
        {
            if (_waiting.TryGetValue(code, out var found) && found.Observed.Equals(sender) is false)
            {
                partner = found;
                _waiting.Remove(code);
            }
            else
            {
                _waiting[code] = new Waiting(sender, local, DateTime.UtcNow);
            }
        }

        if (partner is not { } other)
        {
            Console.WriteLine($"[{code}] en attente : {sender}");
            return;
        }

        Console.WriteLine($"[{code}] appariés : {other.Observed} et {sender}");

        // Chacun reçoit l'adresse publique de l'autre, plus ses adresses locales
        // pour le cas où les deux seraient sous le même toit.
        socket.SendTo(Wire.Partner([sender, .. local]), new IPEndPoint(other.Observed.Address, other.Observed.Port));
        socket.SendTo(Wire.Partner([other.Observed, .. other.Local]), raw);
    }

    /// <summary>
    /// Ramène une adresse IPv4 vue à travers une socket bi-pile à sa forme IPv4.
    /// </summary>
    private static IPEndPoint Normalize(IPEndPoint endpoint)
        => endpoint.Address.IsIPv4MappedToIPv6
            ? new IPEndPoint(endpoint.Address.MapToIPv4(), endpoint.Port)
            : endpoint;
}
