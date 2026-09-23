using System.Text.Json;
using System.Text.Json.Nodes;
using Linkpearl.Core.Safety;

namespace Linkpearl.Core.Manifest;

/// <summary>
/// Retire de la configuration SimpleHeels ce qui ne sert pas au décalage et
/// en dit trop.
/// </summary>
/// <remarks>
/// Relevé dans SimpleHeels 162466c (IpcCharacterConfig) : les positions d'emote
/// et de familier sont des coordonnées absolues dans le monde, les étiquettes
/// sont des chaînes libres écrites par d'autres plugins, <c>E</c> révèle que le
/// plugin Echo est installé. Aucun n'est nécessaire au décalage.
/// </remarks>
public static class HeelsSanitizer
{
    public static IReadOnlyList<string> Removed { get; } =
        ["EmotePosition", "MinionPosition", "Tags", "E", "PluginVersion"];

    private const int MaxDepth = 8;

    public static string? Sanitize(string json)
    {
        if (JsonShape.IsObject(json, MaxDepth) is false)
            return null;

        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
                return null;

            foreach (var name in Removed)
                root.Remove(name);

            return root.ToJsonString();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
