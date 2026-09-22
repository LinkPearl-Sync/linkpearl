using System.Net;

namespace Linkpearl.Probe;

public enum Kind : byte
{
    ClassifyRequest = 1,
    ClassifyReply   = 2,
    Register        = 3,
    Partner         = 4,
    Punch           = 5,
    PunchAck        = 6,
}

/// <summary>
/// Format de trame de la sonde. Volontairement minimal et sans chiffrement :
/// cet outil ne transporte rien de sensible et ne survivra pas au jalon 5.
/// </summary>
public static class Wire
{
    public static byte[] ClassifyRequest() => [(byte)Kind.ClassifyRequest];

    public static byte[] ClassifyReply(byte portIndex, IPEndPoint observed)
    {
        var writer = new List<byte> { (byte)Kind.ClassifyReply, portIndex };
        WriteEndPoint(writer, observed);
        return writer.ToArray();
    }

    public static byte[] Register(string code, IReadOnlyList<IPEndPoint> local)
    {
        var writer = new List<byte> { (byte)Kind.Register };
        WriteString(writer, code);
        writer.Add((byte)local.Count);
        foreach (var endpoint in local)
            WriteEndPoint(writer, endpoint);
        return writer.ToArray();
    }

    public static byte[] Partner(IReadOnlyList<IPEndPoint> candidates)
    {
        var writer = new List<byte> { (byte)Kind.Partner, (byte)candidates.Count };
        foreach (var endpoint in candidates)
            WriteEndPoint(writer, endpoint);
        return writer.ToArray();
    }

    public static byte[] Punch(string code, bool ack)
    {
        var writer = new List<byte> { (byte)(ack ? Kind.PunchAck : Kind.Punch) };
        WriteString(writer, code);
        return writer.ToArray();
    }

    public static Kind KindOf(ReadOnlySpan<byte> frame) => (Kind)frame[0];

    public static (byte PortIndex, IPEndPoint Observed) ReadClassifyReply(ReadOnlySpan<byte> frame)
    {
        var offset = 2;
        return (frame[1], ReadEndPoint(frame, ref offset));
    }

    public static (string Code, List<IPEndPoint> Local) ReadRegister(ReadOnlySpan<byte> frame)
    {
        var offset = 1;
        var code = ReadString(frame, ref offset);
        var count = frame[offset++];
        var local = new List<IPEndPoint>(count);
        for (var i = 0; i < count; i++)
            local.Add(ReadEndPoint(frame, ref offset));
        return (code, local);
    }

    public static List<IPEndPoint> ReadPartner(ReadOnlySpan<byte> frame)
    {
        var offset = 2;
        var candidates = new List<IPEndPoint>(frame[1]);
        for (var i = 0; i < frame[1]; i++)
            candidates.Add(ReadEndPoint(frame, ref offset));
        return candidates;
    }

    public static string ReadPunchCode(ReadOnlySpan<byte> frame)
    {
        var offset = 1;
        return ReadString(frame, ref offset);
    }

    private static void WriteEndPoint(List<byte> writer, IPEndPoint endpoint)
    {
        var address = endpoint.Address.GetAddressBytes();
        writer.Add((byte)address.Length);
        writer.AddRange(address);
        writer.Add((byte)(endpoint.Port >> 8));
        writer.Add((byte)endpoint.Port);
    }

    private static IPEndPoint ReadEndPoint(ReadOnlySpan<byte> frame, ref int offset)
    {
        var length = frame[offset++];
        var address = new IPAddress(frame.Slice(offset, length));
        offset += length;
        var port = (frame[offset] << 8) | frame[offset + 1];
        offset += 2;
        return new IPEndPoint(address, port);
    }

    private static void WriteString(List<byte> writer, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        writer.Add((byte)bytes.Length);
        writer.AddRange(bytes);
    }

    private static string ReadString(ReadOnlySpan<byte> frame, ref int offset)
    {
        var length = frame[offset++];
        var value = System.Text.Encoding.UTF8.GetString(frame.Slice(offset, length));
        offset += length;
        return value;
    }
}
