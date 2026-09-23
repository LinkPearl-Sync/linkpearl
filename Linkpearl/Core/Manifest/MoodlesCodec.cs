using System.Buffers.Binary;
using System.Text;

namespace Linkpearl.Core.Manifest;

/// <summary>Un statut Moodles, membre pour membre.</summary>
public sealed record MoodleStatus(
    Guid Id, int IconId, string Title, string Description, string CustomFxPath, long ExpiresAt,
    int Type, uint Modifiers, int Stacks, int StackSteps, Guid ChainedStatus, int ChainTrigger,
    string Applier, string Dispeller);

/// <summary>
/// Le format binaire de Moodles, décodé à la main et dans ses bornes.
/// </summary>
/// <remarks>
/// Moodles sérialise <c>List&lt;MyStatus&gt;</c> par MemoryPack 1.21.4, chaînes
/// en UTF-16 (relevé dans Moodles f4b7578). Décodé ici plutôt que par la
/// bibliothèque : le noyau ne dépend de rien, et une entrée venue d'un pair se
/// lit octet par octet, sans réflexion ni allocation dictée par elle. Le test
/// d'aller-retour contre la vraie bibliothèque attrape toute dérive.
///
/// Un objet d'un autre nombre de membres est refusé : un Moodles futur qui
/// ajouterait un champ apporterait peut-être un nouvel identifiant.
/// </remarks>
public static class MoodlesCodec
{
    public const byte MemberCount = 14;
    public const int MaxStatuses = 64;
    public const int MaxStringLength = 1024;

    public static bool TryDecode(ReadOnlySpan<byte> data, out IReadOnlyList<MoodleStatus> statuses)
    {
        statuses = [];
        var reader = new Reader(data);

        if (reader.TryInt32(out var count) is false || count < 0 || count > MaxStatuses)
            return false;

        var list = new List<MoodleStatus>(count);

        for (var i = 0; i < count; i++)
        {
            if (reader.TryByte(out var members) is false || members != MemberCount)
                return false;

            if (reader.TryGuid(out var id) is false
                || reader.TryInt32(out var icon) is false
                || reader.TryString(out var title) is false
                || reader.TryString(out var description) is false
                || reader.TryString(out var fx) is false
                || reader.TryInt64(out var expires) is false
                || reader.TryInt32(out var type) is false
                || reader.TryUInt32(out var modifiers) is false
                || reader.TryInt32(out var stacks) is false
                || reader.TryInt32(out var steps) is false
                || reader.TryGuid(out var chained) is false
                || reader.TryInt32(out var trigger) is false
                || reader.TryString(out var applier) is false
                || reader.TryString(out var dispeller) is false)
                return false;

            list.Add(new MoodleStatus(id, icon, title, description, fx, expires, type, modifiers,
                                      stacks, steps, chained, trigger, applier, dispeller));
        }

        if (reader.AtEnd is false)
            return false;

        statuses = list;
        return true;
    }

    public static byte[] Encode(IReadOnlyList<MoodleStatus> statuses)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode);

        writer.Write(statuses.Count);

        foreach (var s in statuses)
        {
            writer.Write(MemberCount);
            writer.Write(s.Id.ToByteArray());
            writer.Write(s.IconId);
            WriteString(writer, s.Title);
            WriteString(writer, s.Description);
            WriteString(writer, s.CustomFxPath);
            writer.Write(s.ExpiresAt);
            writer.Write(s.Type);
            writer.Write(s.Modifiers);
            writer.Write(s.Stacks);
            writer.Write(s.StackSteps);
            writer.Write(s.ChainedStatus.ToByteArray());
            writer.Write(s.ChainTrigger);
            WriteString(writer, s.Applier);
            WriteString(writer, s.Dispeller);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        writer.Write(value.Length);
        writer.Write(Encoding.Unicode.GetBytes(value));
    }

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private ReadOnlySpan<byte> _rest = data;

        public readonly bool AtEnd => _rest.IsEmpty;

        public bool TryByte(out byte value)
        {
            value = 0;
            if (_rest.Length < 1) return false;
            value = _rest[0];
            _rest = _rest[1..];
            return true;
        }

        public bool TryInt32(out int value)
        {
            value = 0;
            if (_rest.Length < 4) return false;
            value = BinaryPrimitives.ReadInt32LittleEndian(_rest);
            _rest = _rest[4..];
            return true;
        }

        public bool TryUInt32(out uint value)
        {
            value = 0;
            if (_rest.Length < 4) return false;
            value = BinaryPrimitives.ReadUInt32LittleEndian(_rest);
            _rest = _rest[4..];
            return true;
        }

        public bool TryInt64(out long value)
        {
            value = 0;
            if (_rest.Length < 8) return false;
            value = BinaryPrimitives.ReadInt64LittleEndian(_rest);
            _rest = _rest[8..];
            return true;
        }

        public bool TryGuid(out Guid value)
        {
            value = Guid.Empty;
            if (_rest.Length < 16) return false;
            value = new Guid(_rest[..16]);
            _rest = _rest[16..];
            return true;
        }

        /// <summary>Chaîne UTF-16 de MemoryPack. Une chaîne nulle (−1) est refusée : Moodles n'en écrit pas.</summary>
        public bool TryString(out string value)
        {
            value = "";
            if (TryInt32(out var length) is false || length < 0 || length > MaxStringLength)
                return false;

            if (_rest.Length < length * 2) return false;
            value = Encoding.Unicode.GetString(_rest[..(length * 2)]);
            _rest = _rest[(length * 2)..];
            return true;
        }
    }
}
