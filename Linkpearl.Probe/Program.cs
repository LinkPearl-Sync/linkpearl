using Linkpearl.Probe;

// Sonde de traversée de NAT du jalon 2. Instrument de mesure, pas brique du
// produit : rien ici ne survivra au jalon 5.

if (args.Length == 0)
{
    Console.WriteLine("""
        Sonde Linkpearl.

          lpprobe server [port]                     à lancer sur le VPS (défaut 47800)
          lpprobe classify <hôte> [port] [--v6 <adr>]  classe votre NAT, sans personne d'autre
          lpprobe pair <hôte> <code> [port]         essaie de joindre un autre exemplaire

        Le mode « pair » se lance des deux côtés avec le même code. Les deux
        machines doivent être sur des réseaux différents pour que la mesure ait
        un sens : une ligne fixe d'un côté, un partage de connexion mobile de
        l'autre est le cas le plus défavorable, donc le plus instructif.
        """);
    return;
}

switch (args[0])
{
    case "server":
        new ProbeServer().Run(args.Length > 1 ? int.Parse(args[1]) : 47800);
        break;

    case "classify":
    {
        var v6Index = Array.IndexOf(args, "--v6");
        var v6 = v6Index >= 0 && v6Index + 1 < args.Length ? args[v6Index + 1] : null;
        var port = args.Length > 2 && int.TryParse(args[2], out var p) ? p : 47800;
        new ProbeClient(args[1], port, v6).Classify();
        break;
    }

    case "pair":
        new ProbeClient(args[1], args.Length > 3 ? int.Parse(args[3]) : 47800).Pair(args[2]);
        break;

    default:
        Console.WriteLine($"Mode inconnu : {args[0]}");
        break;
}
