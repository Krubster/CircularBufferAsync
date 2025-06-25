using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Framework.Metrics
{
    public class ThreadMetricsService
    {
        private readonly ConcurrentDictionary<int, ThreadMetrics> _threadMetrics = new();

        public void RecordSleep(int threadId, long ticks)
        {
            var metrics = _threadMetrics.GetOrAdd(threadId, id => new ThreadMetrics());
            Interlocked.Add(ref metrics.SleepTicks, ticks);
        }

        public void RecordYield(int threadId, long ticks)
        {
            var metrics = _threadMetrics.GetOrAdd(threadId, id => new ThreadMetrics());
            Interlocked.Increment(ref metrics.YieldCount);
        }

        public ThreadMetricsSnapshot GetSnapshot()
        {
            var snapshot = new ThreadMetricsSnapshot();
            foreach (var (_, metrics) in _threadMetrics)
            {
                snapshot.TotalSleepTicks += Interlocked.Read(ref metrics.SleepTicks);
                snapshot.TotalYieldCount += Interlocked.Read(ref metrics.YieldCount);
            }
            return snapshot;
        }
    }

    public class ThreadMetrics
    {
        public long SleepTicks;
        public long YieldCount;
    }
    public class ThreadMetricsSnapshot
    {
        public long TotalSleepTicks { get; set; }
        public long TotalYieldCount { get; set; }
    }

}
