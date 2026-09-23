using System.Text.Json;
using Linkpearl.Core.Abstractions;

namespace Linkpearl.Core.Manifest;

/// <summary>
/// Les animations, VFX et sons moddés déjà vus sur le personnage local, par job.
/// </summary>
/// <remarks>
/// Penumbra ne dit quelle animation est remplacée qu'au moment où le jeu la
/// charge. Sans mémoire, une pose assise ou une idle n'est envoyée que si elle
/// joue à l'instant où le manifeste est construit : l'autre voit la pose par
/// défaut jusqu'au prochain changement. Retenir ce qui a été vu permet de
/// l'envoyer même quand ça ne joue pas.
///
/// Un chemin vu sous un seul job n'est rendu que pour lui : une animation de
/// combat moddée pour le barde n'a rien à faire dans le manifeste du paladin.
/// Revu sous un second job, il devient commun, comme une idle ou un emote.
///
/// Rien ici ne vient du réseau : les chemins sont ceux que le jeu local a
/// chargés. Le plafond borne tout de même le fichier, qu'une session très
/// longue ne doit pas faire grossir sans fin.
/// </remarks>
public sealed class TransientMemory(IClock clock)
{
    public const int MaxPaths = 4096;

    private const int FormatVersion = 1;

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    private sealed class Entry
    {
        public HashSet<uint> Jobs { get; } = [];

        public bool Common { get; set; }

        public DateTimeOffset LastSeen { get; set; }
    }

    /// <summary>Retient un chemin vu sous ce job ; vrai si le manifeste doit changer.</summary>
    public bool Record(string gamePath, uint job)
    {
        var path = Normalize(gamePath);
        var now = clock.UtcNow;

        if (_entries.TryGetValue(path, out var entry) is false)
        {
            if (_entries.Count >= MaxPaths)
                return false;

            entry = new Entry { LastSeen = now };
            entry.Jobs.Add(job);
            _entries[path] = entry;
            return true;
        }

        entry.LastSeen = now;

        if (entry.Common || entry.Jobs.Add(job) is false)
            return false;

        entry.Common = true;
        return true;
    }

    public IReadOnlyCollection<string> PathsFor(uint job)
        => _entries.Where(e => e.Value.Common || e.Value.Jobs.Contains(job)).Select(e => e.Key).ToList();

    /// <summary>Oublie un chemin, par exemple redevenu vanilla.</summary>
    public void Forget(string gamePath) => _entries.Remove(Normalize(gamePath));

    /// <summary>Oublie ce qui n'a pas été revu depuis <paramref name="olderThan"/>.</summary>
    public int Purge(TimeSpan olderThan)
    {
        var limit = clock.UtcNow - olderThan;
        var stale = _entries.Where(e => e.Value.LastSeen < limit).Select(e => e.Key).ToList();

        foreach (var path in stale)
            _entries.Remove(path);

        return stale.Count;
    }

    public string ToJson()
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", FormatVersion);
            writer.WriteStartArray("e");

            foreach (var (path, entry) in _entries)
            {
                writer.WriteStartObject();
                writer.WriteString("p", path);
                writer.WriteBoolean("c", entry.Common);
                writer.WriteStartArray("j");

                foreach (var job in entry.Jobs.Order())
                    writer.WriteNumberValue(job);

                writer.WriteEndArray();
                writer.WriteNumber("s", entry.LastSeen.ToUnixTimeSeconds());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Relit une mémoire ; un fichier abîmé donne une mémoire vide, jamais une exception.</summary>
    /// <remarks>
    /// Perdre la mémoire ne coûte qu'un envoi incomplet jusqu'à la prochaine
    /// animation jouée. Empêcher le plugin de démarrer pour un fichier abîmé
    /// coûterait bien plus.
    /// </remarks>
    public static TransientMemory FromJson(string json, IClock clock)
    {
        var memory = new TransientMemory(clock);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || root.TryGetProperty("v", out var version) is false
                || version.ValueKind != JsonValueKind.Number
                || version.GetInt32() != FormatVersion
                || root.TryGetProperty("e", out var entries) is false
                || entries.ValueKind != JsonValueKind.Array)
                return memory;

            foreach (var item in entries.EnumerateArray())
            {
                if (memory._entries.Count >= MaxPaths)
                    break;

                var entry = new Entry
                {
                    Common = item.GetProperty("c").GetBoolean(),
                    LastSeen = DateTimeOffset.FromUnixTimeSeconds(item.GetProperty("s").GetInt64()),
                };

                foreach (var job in item.GetProperty("j").EnumerateArray())
                    entry.Jobs.Add(job.GetUInt32());

                memory._entries[Normalize(item.GetProperty("p").GetString() ?? string.Empty)] = entry;
            }

            return memory;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException
                                      or FormatException or ArgumentOutOfRangeException)
        {
            return new TransientMemory(clock);
        }
    }

    private static string Normalize(string gamePath) => gamePath.Replace('\\', '/').ToLowerInvariant();
}
