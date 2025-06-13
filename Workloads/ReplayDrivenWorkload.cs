using System.Buffers.Binary;
using CircularBufferAsync;
using CircularBufferAsync.LoadProfiles;
using Framework.Buffers;
using Test;

public class ReplayDrivenWorkload : IPacketWorkload
{
    private readonly HistogramReplayProfile _replay;
    private readonly int _cpuWorkPerByte;

    public ReplayDrivenWorkload(HistogramReplayProfile replay, int cpuWorkPerByte = 10)
    {
        _replay = replay;
        _cpuWorkPerByte = cpuWorkPerByte;
    }

    public (int, int, SpanWriter.SpanOwner?) ProcessBuffer(ReadOnlySpan<byte> buffer, NetState state)
    {
        var writer = new SpanWriter(2048, resize: true);
        int offset = 0, processed = 0;

        while (offset + 2 <= buffer.Length)
        {
            ushort len = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(offset, 2));
            if (offset + 2 + len > buffer.Length)
                break;

            var packet = buffer.Slice(offset + 2, len);
            Thread.SpinWait(packet.Length * _cpuWorkPerByte);
            processed++;

            if (!_replay.TryGetNext(out int responseSize))
                continue;

            int payloadLen = responseSize - 2;
            writer.WriteLE((ushort)payloadLen);
            var span = writer.RawBuffer.Slice(writer.Position, payloadLen);
            Random.Shared.NextBytes(span);
            writer.Seek(payloadLen, SeekOrigin.Current);

            offset += 2 + len;
        }

        return processed > 0 ? (processed, writer.BytesWritten, writer.ToSpan()) : (0, 0, null);
    }
}