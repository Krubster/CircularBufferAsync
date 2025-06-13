using System.Buffers.Binary;
using Framework.Buffers;
using CircularBufferAsync;
using Test;

public class CpuIntensiveWorkload : IPacketWorkload
{
    private readonly int _workPerByte;

    public CpuIntensiveWorkload(int workPerByte = 1000000000)
    {
        _workPerByte = workPerByte;
    }

    public (int, int, SpanWriter.SpanOwner?) ProcessBuffer(ReadOnlySpan<byte> buffer, NetState state)
    {
        var writer = new SpanWriter(65535);
        int offset = 0, processed = 0;

        while (offset + 2 <= buffer.Length)
        {
            ushort len = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(offset, 2));
            if (offset + 2 + len > buffer.Length)
                break;

            var packet = buffer.Slice(offset + 2, len);
            Thread.SpinWait(packet.Length * _workPerByte);
            processed++;

            writer.WriteLE((ushort)len);
            for (int i = 0; i < len; i++)
                writer.Write((byte)(packet[i] ^ 0x5A));

            offset += 2 + len;
        }

        return processed > 0 ? (processed, writer.BytesWritten, writer.ToSpan()) : (0, 0, null);
    }
}