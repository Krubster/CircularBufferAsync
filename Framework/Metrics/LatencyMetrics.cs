using System.Buffers.Binary;
using System.Diagnostics;

namespace Framework.Metrics
{
    public class LatencyMetrics
    {
        public LatencyMetrics() { }
     
        public static void MarkPacket(Span<byte> payload, uint timestamp)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(2, 4), timestamp);
        }

        public static void MarkPacket(Span<byte> payload)
        {
            var now = (uint)(Stopwatch.GetTimestamp() * 1_000 / Stopwatch.Frequency);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(2, 4), now);
        }

        public static uint ExtractTimestamp(Span<byte> payload)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(2, 4));
        }
    }

    public class LatencyAggregator
    {
        private long _sum;
        private int _count;
        public double LastLatency { get; private set; } = 0;
        public void Add(uint latency)
        {
            _sum += latency;
            _count++;
        }

        public double Average()
        {
            if (_count == 0)
                return LastLatency;

            LastLatency = (double)_sum / (double)_count;
            return LastLatency;
        }

        public void Reset()
        {
            _sum = 0;
            _count = 0;
        }
    }
}
