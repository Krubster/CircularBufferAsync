using System.Buffers.Binary;
using System.Diagnostics;
using Framework.Buffers;
using NETwork;

public class CpuIntensiveProcessor : IPacketProcessor
{
    public int CpuLoad
    {
        get => _workUs; set
        {
            if (value < 1)
                throw new ArgumentOutOfRangeException(nameof(value), "CpuLoad must be at least 1");
            _workUs = value;
        }
    }
    private int _workUs;

    public CpuIntensiveProcessor(int workUs = 10)
    {
        _workUs = workUs;
    }

    public (int, List<uint>) ProcessBuffer(ReadOnlySpan<byte> inBuffer, NetState state)
    {
        int offset = 0, processed = 0;
        List<uint> timestamps = new List<uint>();
        while (offset + 2 <= inBuffer.Length)
        {
            ushort len = BinaryPrimitives.ReadUInt16LittleEndian(inBuffer.Slice(offset, 2));
            if (offset + 2 + len > inBuffer.Length)
                break;
            
            uint timestamp = BinaryPrimitives.ReadUInt32LittleEndian(inBuffer.Slice(offset + 2, 4));
            timestamps.Add(timestamp);

            var packet = inBuffer.Slice(offset + 2, len);
            processed++;
            long ticksPerMicrosecond = Stopwatch.Frequency / 1_000_000;
            long targetTicks = _workUs * ticksPerMicrosecond;

            long start = Stopwatch.GetTimestamp();
            long now = Stopwatch.GetTimestamp();
            while (now - start < targetTicks)
            {
                Thread.SpinWait(10); // активное ожидание
                now = Stopwatch.GetTimestamp();
            }
            offset += 2 + len;
        }

        return (processed, timestamps);
    }
}