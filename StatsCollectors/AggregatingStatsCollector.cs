using System.Collections.Concurrent;

namespace Test.Stats
{
    public class AggregatingStatsCollector : IStatsCollector, IDisposable
    {
        private readonly ConcurrentBag<ConnectionStats> _stats = new();
        private readonly System.Timers.Timer _flushTimer;

        public AggregatingStatsCollector(double intervalSeconds = 5.0)
        {
            _flushTimer = new System.Timers.Timer(intervalSeconds * 1000);
            _flushTimer.Elapsed += (_, _) => Flush();
            _flushTimer.AutoReset = true;
            _flushTimer.Start();
        }

        public void Report(ConnectionStats stats)
        {
            _stats.Add(stats);
        }

        public void Flush()
        {
            if (_stats.IsEmpty)
                return;

            long totalRecv = 0, totalSent = 0, totalPackets = 0;
            int count = 0;

            foreach (var s in _stats)
            {
                totalRecv += s.BytesReceived;
                totalSent += s.BytesSent;
                totalPackets += s.PacketsProcessed;
                count++;
            }

            Console.WriteLine(
                $"[AGGREGATE] Connections: {count} | " +
                $"TotalRecv: {totalRecv} B | TotalSent: {totalSent} B | " +
                $"TotalPackets: {totalPackets} | " +
                $"GC: {GC.CollectionCount(0)} / {GC.CollectionCount(1)} / {GC.CollectionCount(2)}");

            // Optional: reset after flush
            _stats.Clear();
        }

        public void Dispose()
        {
            _flushTimer.Dispose();
        }
    }
}
