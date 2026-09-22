using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Linkpearl.Core.Cache;
using Linkpearl.Core.Safety;

namespace Linkpearl.Core.Manifest;

/// <summary>
/// Encodage canonique et compression du manifeste.
/// </summary>
/// <remarks>
/// L'encodage est écrit à la main avec <see cref="Utf8JsonWriter"/> plutôt que
/// sérialisé par réflexion : l'ordre des propriétés fait partie du contrat,
/// puisque c'est lui qui rend le hash du manifeste reproductible d'une machine
/// à l'autre. La lecture passe par <see cref="JsonDocument"/>, qui n'utilise pas
/// non plus la réflexion et n'instancie rien que nous n'ayons validé.
///
/// Noms de champs courts : le manifeste d'un personnage lourd porte plusieurs
/// centaines d'entrées, et ces octets se paient à chaque rencontre.
/// </remarks>
public static class ManifestCodec
{
    public static byte[] Encode(CharacterManifest manifest)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", manifest.Version);

            writer.WriteStartArray("r");
            foreach (var replacement in manifest.Replacements)
            {
                writer.WriteStartObject();
                writer.WriteString("h", replacement.Hash.ToHex());
                writer.WriteNumber("s", replacement.Size);
                writer.WriteStartArray("p");
                foreach (var path in replacement.GamePaths)
                    writer.WriteStringValue(path);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteString("m", manifest.MetaManipulations);

            if (manifest.GlamourerState is null)
                writer.WriteNull("g");
            else
                writer.WriteString("g", manifest.GlamourerState);

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Identité du manifeste. C'est elle qu'on compare pour savoir si quoi que
    /// ce soit a changé, et elle seule qu'on annonce à un pair.
    /// </summary>
    public static BlobHash HashOf(CharacterManifest manifest)
        => BlobHash.OfContent(Encode(manifest));

    public static byte[] Compress(CharacterManifest manifest)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
            brotli.Write(Encode(manifest));

        return output.ToArray();
    }

    public static bool TryDecompress(
        ReadOnlySpan<byte> compressed, Quotas quotas, out CharacterManifest? manifest, out string? rejection)
    {
        manifest = null;

        if (compressed.Length > quotas.MaxManifestCompressedBytes)
        {
            rejection = $"manifeste compressé trop gros ({compressed.Length} octets, plafond {quotas.MaxManifestCompressedBytes})";
            return false;
        }

        if (TryInflate(compressed, quotas.MaxManifestDecompressedBytes, out var plain, out rejection) is false)
            return false;

        return TryParse(plain, quotas, out manifest, out rejection);
    }

    /// <summary>
    /// Détend le flux en s'arrêtant au plafond, sans jamais faire confiance à la
    /// taille annoncée par l'émetteur.
    /// </summary>
    private static bool TryInflate(ReadOnlySpan<byte> compressed, int cap, out byte[] plain, out string? rejection)
    {
        plain = [];

        using var input = new MemoryStream(compressed.ToArray(), writable: false);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();

        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            int read;
            while ((read = brotli.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > cap)
                {
                    rejection = $"plafond de décompression dépassé ({cap} octets)";
                    return false;
                }

                output.Write(buffer, 0, read);
            }
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException)
        {
            // Brotli signale une entrée corrompue par InvalidOperationException et
            // non par InvalidDataException. L'entrée vient d'un pair : toute
            // défaillance du décodeur devient un refus, jamais une exception qui
            // remonterait dans la boucle de synchronisation.
            rejection = "flux compressé illisible";
            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        plain = output.ToArray();
        rejection = null;
        return true;
    }

    private static bool TryParse(byte[] plain, Quotas quotas, out CharacterManifest? manifest, out string? rejection)
    {
        manifest = null;

        try
        {
            using var document = JsonDocument.Parse(plain);
            var root = document.RootElement;

            if (root.ValueKind is not JsonValueKind.Object)
            {
                rejection = "manifeste : racine non objet";
                return false;
            }

            if (root.TryGetProperty("v", out var versionElement) is false
                || versionElement.TryGetUInt16(out var version) is false)
            {
                rejection = "manifeste : version absente ou illisible";
                return false;
            }

            root.TryGetProperty("r", out var replacementsElement);

            if (replacementsElement.ValueKind is not JsonValueKind.Array)
            {
                rejection = "manifeste : liste de remplacements absente";
                return false;
            }

            if (replacementsElement.GetArrayLength() > quotas.MaxReplacements)
            {
                rejection = $"manifeste : plafond de remplacements dépassé (plafond {quotas.MaxReplacements})";
                return false;
            }

            var replacements = new List<FileReplacement>(replacementsElement.GetArrayLength());
            var totalPaths = 0;

            foreach (var element in replacementsElement.EnumerateArray())
            {
                if (element.ValueKind is not JsonValueKind.Object
                    || element.TryGetProperty("h", out var hashElement) is false
                    || hashElement.ValueKind is not JsonValueKind.String
                    || BlobHash.TryParseHex(hashElement.GetString(), out var hash) is false)
                {
                    rejection = "manifeste : empreinte absente ou malformée";
                    return false;
                }

                if (element.TryGetProperty("s", out var sizeElement) is false
                    || sizeElement.TryGetInt64(out var size) is false
                    || size < 0)
                {
                    rejection = "manifeste : taille absente ou négative";
                    return false;
                }

                if (element.TryGetProperty("p", out var pathsElement) is false
                    || pathsElement.ValueKind is not JsonValueKind.Array
                    || pathsElement.GetArrayLength() == 0)
                {
                    rejection = "manifeste : entrée sans chemin de jeu";
                    return false;
                }

                totalPaths += pathsElement.GetArrayLength();
                if (totalPaths > quotas.MaxGamePaths)
                {
                    rejection = $"manifeste : plafond de chemins de jeu dépassé (plafond {quotas.MaxGamePaths})";
                    return false;
                }

                var paths = new List<string>(pathsElement.GetArrayLength());
                foreach (var pathElement in pathsElement.EnumerateArray())
                {
                    if (pathElement.ValueKind is not JsonValueKind.String)
                    {
                        rejection = "manifeste : chemin de jeu non textuel";
                        return false;
                    }

                    paths.Add(pathElement.GetString()!);
                }

                replacements.Add(new FileReplacement(paths, hash, size));
            }

            if (root.TryGetProperty("m", out var metaElement) is false
                || metaElement.ValueKind is not JsonValueKind.String)
            {
                rejection = "manifeste : manipulations méta absentes";
                return false;
            }

            string? glamourer = null;
            if (root.TryGetProperty("g", out var glamourerElement))
            {
                glamourer = glamourerElement.ValueKind switch
                {
                    JsonValueKind.String => glamourerElement.GetString(),
                    JsonValueKind.Null => null,
                    _ => throw new JsonException("état Glamourer non textuel"),
                };
            }

            manifest = new CharacterManifest(version, replacements, metaElement.GetString()!, glamourer);
            rejection = null;
            return true;
        }
        catch (JsonException e)
        {
            rejection = $"manifeste illisible : {e.Message}";
            return false;
        }
    }
}
