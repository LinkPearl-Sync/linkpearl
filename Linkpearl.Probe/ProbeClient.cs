using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Linkpearl.Probe;

/// <summary>Sonde côté client : classement du NAT, puis essai de traversée.</summary>
public sealed class ProbeClient
{
    private readonly Socket _socket;
    private readonly string _host;
    private readonly int _basePort;

    public ProbeClient(string host, int basePort)
    {
        _host = host;
        _basePort = basePort;

        _socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp) { DualMode = true };
        _socket.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
        _socket.ReceiveTimeout = 3000;
    }

    public int LocalPort => ((IPEndPoint)_socket.LocalEndPoint!).Port;

    public void Classify()
    {
        Console.WriteLine($"Socket locale sur le port {LocalPort}.");
        Console.WriteLine();

        var addresses = Dns.GetHostAddresses(_host);
        var v4 = addresses.FirstOrDefault(a => a.AddressFamily is AddressFamily.InterNetwork);
        var v6 = addresses.FirstOrDefault(a => a.AddressFamily is AddressFamily.InterNetworkV6);

        Console.WriteLine($"Serveur : IPv4 {(v4?.ToString() ?? "aucune")}, IPv6 {(v6?.ToString() ?? "aucune")}");

        if (v6 is not null)
        {
            var observed = Ask(new IPEndPoint(v6, _basePort));
            Console.WriteLine(observed is null
                ? "IPv6 : aucune réponse. Pas d'IPv6 globale utilisable, ou filtrée."
                : $"IPv6 : joignable, vu depuis le serveur comme {observed}. Pas de NAT sur ce chemin.");
        }

        if (v4 is null)
        {
            Console.WriteLine("Pas d'IPv4 sur le serveur : classement du NAT impossible.");
            return;
        }

        var first  = Ask(new IPEndPoint(v4, _basePort));
        var second = Ask(new IPEndPoint(v4, _basePort + 1));

        Console.WriteLine();

        if (first is null || second is null)
        {
            Console.WriteLine("IPv4 : pas de réponse sur les deux ports. Pare-feu sortant, ou serveur injoignable.");
            return;
        }

        Console.WriteLine($"Vu depuis le port {_basePort}     : {first}");
        Console.WriteLine($"Vu depuis le port {_basePort + 1} : {second}");
        Console.WriteLine();

        var cgnat = IsCarrierGrade(first.Address);

        if (first.Port == second.Port)
        {
            Console.WriteLine("NAT à mapping indépendant de la destination.");
            Console.WriteLine("La traversée directe a de bonnes chances de fonctionner.");
        }
        else
        {
            Console.WriteLine("NAT SYMÉTRIQUE : le port public change selon la destination.");
            Console.WriteLine("La traversée directe échouera presque toujours. Le relais sera le chemin normal.");
        }

        if (cgnat)
            Console.WriteLine("L'adresse publique est dans 100.64.0.0/10 : vous êtes derrière un CGNAT opérateur.");
    }

    /// <summary>Essaie de joindre directement un autre exemplaire de la sonde.</summary>
    public void Pair(string code)
    {
        var v4 = Dns.GetHostAddresses(_host).First(a => a.AddressFamily is AddressFamily.InterNetwork);
        var server = new IPEndPoint(v4, _basePort);

        var local = LocalAddresses().Select(a => new IPEndPoint(a, LocalPort)).ToList();

        Console.WriteLine($"Code « {code} ». En attente d'un partenaire, Ctrl+C pour abandonner.");

        List<IPEndPoint>? candidates = null;
        var deadline = Stopwatch.StartNew();

        while (candidates is null && deadline.Elapsed < TimeSpan.FromMinutes(5))
        {
            _socket.SendTo(Wire.Register(code, local), server);
            candidates = Receive(frame => Wire.KindOf(frame) is Kind.Partner ? Wire.ReadPartner(frame) : null);
        }

        if (candidates is null)
        {
            Console.WriteLine("Aucun partenaire ne s'est présenté.");
            return;
        }

        Console.WriteLine($"Partenaire trouvé, {candidates.Count} adresses à essayer :");
        foreach (var candidate in candidates)
            Console.WriteLine($"  {candidate}");

        Punch(code, candidates);
    }

    private void Punch(string code, List<IPEndPoint> candidates)
    {
        var watch = Stopwatch.StartNew();
        var punch = Wire.Punch(code, ack: false);
        var ack = Wire.Punch(code, ack: true);

        // Rafales répétées : le NatPunchModule de LiteNetLib n'envoie que deux
        // paquets sans réessai, ce que cette sonde sert justement à mesurer.
        for (var round = 0; round < 60; round++)
        {
            foreach (var candidate in candidates)
            {
                try
                {
                    _socket.SendTo(punch, candidate);
                }
                catch (SocketException)
                {
                    // candidat injoignable : on continue avec les autres
                }
            }

            var hit = Receive<IPEndPoint>(frame =>
            {
                var kind = Wire.KindOf(frame);
                return kind is Kind.Punch or Kind.PunchAck && Wire.ReadPunchCode(frame) == code
                    ? new IPEndPoint(IPAddress.Any, 0)
                    : null;
            }, out var from);

            if (hit is null)
                continue;

            if (from is IPEndPoint peer)
            {
                _socket.SendTo(ack, peer);
                Console.WriteLine();
                Console.WriteLine($"TRAVERSÉE RÉUSSIE en {watch.ElapsedMilliseconds} ms, avec {peer}.");
                Console.WriteLine($"Tour numéro {round + 1}.");
            }
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"TRAVERSÉE ÉCHOUÉE après {watch.Elapsed.TotalSeconds:F0} s et 60 tours.");
        Console.WriteLine("Pour cette paire, le relais sera obligatoire.");
    }

    private IPEndPoint? Ask(IPEndPoint server)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                _socket.SendTo(Wire.ClassifyRequest(), server);
                var result = Receive(frame =>
                    Wire.KindOf(frame) is Kind.ClassifyReply ? Wire.ReadClassifyReply(frame).Observed : null);

                if (result is not null)
                    return result;
            }
            catch (SocketException)
            {
                // on retente
            }
        }

        return null;
    }

    private T? Receive<T>(Func<byte[], T?> parse) where T : class
        => Receive(parse, out _);

    private T? Receive<T>(Func<byte[], T?> parse, out EndPoint? from) where T : class
    {
        var buffer = new byte[2048];
        from = null;

        try
        {
            EndPoint sender = new IPEndPoint(IPAddress.IPv6Any, 0);
            var read = _socket.ReceiveFrom(buffer, ref sender);
            from = Normalize(sender);
            return parse(buffer[..read]);
        }
        catch (SocketException)
        {
            return null;
        }
    }

    private static EndPoint Normalize(EndPoint endpoint)
        => endpoint is IPEndPoint ip && ip.Address.IsIPv4MappedToIPv6
            ? new IPEndPoint(ip.Address.MapToIPv4(), ip.Port)
            : endpoint;

    private static bool IsCarrierGrade(IPAddress address)
    {
        if (address.AddressFamily is not AddressFamily.InterNetwork)
            return false;

        var bytes = address.GetAddressBytes();
        return bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127;
    }

    private static IEnumerable<IPAddress> LocalAddresses()
        => System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus is System.Net.NetworkInformation.OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily is AddressFamily.InterNetwork && IPAddress.IsLoopback(a) is false);
}
