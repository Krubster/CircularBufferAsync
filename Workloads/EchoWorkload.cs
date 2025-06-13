using CircularBufferAsync;
using System.Buffers.Binary;
using System.Formats.Asn1;
using Framework.Buffers;
using Test;

public class EchoWorkload : IPacketWorkload
{
    private const int MaxPacketSize = 2048; // Предположим worst-case на буфер, можно адаптировать

    public (int, int, SpanWriter.SpanOwner?) ProcessBuffer(ReadOnlySpan<byte> buffer, NetState state)
    {
        var writer = new SpanWriter(MaxPacketSize, resize: true);
        int offset = 0;
        int processed = 0;

        while (offset + 2 <= buffer.Length)
        {
            ushort len = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(offset, 2));
            if (offset + 2 + len > buffer.Length)
                break;

            var packet = buffer.Slice(offset + 2, len);

            // Запись [2 байта длины] + payload
            writer.WriteLE((ushort)packet.Length);
            writer.Write(packet);

            processed++;
            offset += 2 + len;
        }

        return processed > 0 ? (processed, writer.BytesWritten, writer.ToSpan()) : (0, 0, null);
    }
}