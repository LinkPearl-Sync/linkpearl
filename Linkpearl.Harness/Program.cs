using Linkpearl.Harness;

// Banc de mesure du jalon 2. Ne construit rien de définitif : il répond à une
// seule question, celle de savoir si LiteNetLib peut porter 298 Mo dans un
// temps acceptable, et sous quelles conditions.

if (args.Length > 0 && args[0] == "vectors")
{
    Console.WriteLine(VectorGenerator.Build());
    return;
}

if (args.Length > 0 && args[0] == "endtoend")
{
    var cache = ArgString("--cache", "/mnt/c/Users/yann/AppData/Local/Linkpearl/cache");
    var manifest = ArgString("--manifest", Path.Combine(Path.GetDirectoryName(cache)!, "capture.json.br"));

    await EndToEndRun.ExecuteAsync(
        new EndToEndSettings(
            SourceCacheRoot: cache,
            ManifestPath: manifest,
            DataChannels: Arg("--channels", 24),
            LatencyMs: Arg("--latency", 0),
            LossPercent: Arg("--loss", 0),
            RateLimited: Array.IndexOf(args, "--no-limit") < 0,
            BlockSize: Arg("--block", 16) * 1024,
            SynthesizeFromCache: Array.IndexOf(args, "--from-cache") >= 0),
        CancellationToken.None);
    return;
}

string ArgString(string name, string fallback)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
}

var totalBytes  = Arg("--size", 298) * 1024L * 1024L;
var latencies   = ArgList("--latency", [20, 60, 150]);
var channelSets = ArgList("--channels", [1, 8, 24]);
var losses      = ArgList("--loss", [0, 1]);
var pollMs      = Arg("--poll", 15);
var blockSize   = Arg("--block", 16) * 1024;

Console.WriteLine($"Corpus {totalBytes / 1024 / 1024} Mo, blocs de {blockSize / 1024} Kio, "
                + $"PollEvents toutes les {pollMs} ms.");
Console.WriteLine();
Console.WriteLine($"{"canaux",7} {"latence",8} {"perte",6} {"durée",9} {"débit",11} {"ping",6} {"perte vue",10} {"mémoire",9}");
Console.WriteLine(new string('-', 76));

foreach (var loss in losses)
foreach (var latency in latencies)
foreach (var channels in channelSets)
{
    var settings = new RunSettings(totalBytes, channels + 1, blockSize, latency, latency / 10, loss, pollMs, Seed: 1);
    var result = ThroughputRun.Execute(settings);

    Console.WriteLine($"{channels,7} {latency + " ms",8} {loss + " %",6} "
                    + $"{result.Seconds,7:F1} s {result.MegabytesPerSecond,8:F2} Mo/s "
                    + $"{result.ReportedPing,4} ms {result.ReportedLossPercent,8:F1} % "
                    + $"{result.PeakWorkingSetDeltaBytes / 1024 / 1024,6} Mo");
}

int Arg(string name, int fallback)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value) ? value : fallback;
}

int[] ArgList(string name, int[] fallback)
{
    var index = Array.IndexOf(args, name);
    if (index < 0 || index + 1 >= args.Length)
        return fallback;

    return args[index + 1].Split(',').Select(int.Parse).ToArray();
}
