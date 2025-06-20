using System.Buffers.Binary;
using Framework.Buffers;
using NETwork;

public class CpuIntensiveProcessor : IPacketProcessor
{
    public int CpuLoad
    {
        get => _workPerByte; set
        {
            if (value < 1)
                throw new ArgumentOutOfRangeException(nameof(value), "CpuLoad must be at least 1");
            _workPerByte = value;
        }
    }
    private int _workPerByte;

    public CpuIntensiveProcessor(int workPerByte = 10)
    {
        _workPerByte = workPerByte;
    }

    public int ProcessBuffer(ReadOnlySpan<byte> inBuffer, NetState state)
    {
        int offset = 0, processed = 0;

        while (offset + 2 <= inBuffer.Length)
        {
            ushort len = BinaryPrimitives.ReadUInt16LittleEndian(inBuffer.Slice(offset, 2));
            if (offset + 2 + len > inBuffer.Length)
                break;

            var packet = inBuffer.Slice(offset + 2, len);
            Thread.SpinWait(packet.Length * _workPerByte);
            processed++;

            offset += 2 + len;
        }

        return processed;
    }
}