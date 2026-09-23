using System.Text;
using System.Text.Json;

namespace Linkpearl.Core.Safety;

/// <summary>Vérifie qu'une chaîne venue d'un pair est un objet JSON de profondeur bornée.</summary>
/// <remarks>
/// La profondeur est bornée par le lecteur lui-même : un JSON imbriqué sur
/// des milliers de niveaux épuiserait la pile d'un parseur récursif, chez
/// nous ou chez le plugin à qui on le passe.
/// </remarks>
public static class JsonShape
{
    public static bool IsObject(string json, int maxDepth)
    {
        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json), new JsonReaderOptions { MaxDepth = maxDepth });

            if (reader.Read() is false || reader.TokenType is not JsonTokenType.StartObject)
                return false;

            reader.Skip();
            return reader.Read() is false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
