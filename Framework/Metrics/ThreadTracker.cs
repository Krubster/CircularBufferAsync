using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Framework.Metrics
{
    public static class TrackerServices
    {
        public static readonly ThreadMetricsService ThreadMetrics = new();
        public static readonly ContentionMetricsService ContentionMetrics = new();
    }

    public static class ThreadTracker
    {
        [ThreadStatic]
        private static ThreadMetricsService? _currentMetrics;

        public static void BindMetrics(ThreadMetricsService metrics)
            => _currentMetrics = metrics;

        public static void SafeSleep(int milliseconds)
        {
            if (_currentMetrics == null)
            {
                Thread.Sleep(milliseconds);
                return;
            }
            long start = Stopwatch.GetTimestamp();
            Thread.Sleep(milliseconds);
            long end = Stopwatch.GetTimestamp();
            var elapsed = end - start;
            _currentMetrics.RecordSleep(Environment.CurrentManagedThreadId, elapsed);
        }

        public static void SafeYield()
        {
            if (_currentMetrics == null)
            {
                Thread.Yield();
                return;
            }

            long start = Stopwatch.GetTimestamp();
            Thread.Yield();
            long end = Stopwatch.GetTimestamp();
            var elapsed = end - start;
            _currentMetrics.RecordYield(Environment.CurrentManagedThreadId, elapsed);
        }
    }

}
