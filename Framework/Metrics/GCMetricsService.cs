using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NETwork;

namespace Framework.Metrics
{
    public class GCUsageMetrics
    {
        public void GenerateMetrics(ConnectionStats stats)
        {

            stats.AddMetric("GC Heap", GC.GetTotalMemory(false));

            stats.AddMetric("GC Gen0",GC.CollectionCount(0));
            stats.AddMetric("GC Gen1", GC.CollectionCount(1));
            stats.AddMetric("GC Gen2", GC.CollectionCount(2));
        }
    }

    public class GCMetricsService : IDisposable
    {
        private long _totalPauseTicks;

        public GCMetricsService()
        {
            GCNotifier.RegisterHandler(OnGcPause);
        }

        private void OnGcPause(long pauseMs)
        {
            Interlocked.Add(ref _totalPauseTicks, TimeSpan.FromMilliseconds(pauseMs).Ticks);
        }

        public long GetAndResetTotalPause()
        {
            return Interlocked.Exchange(ref _totalPauseTicks, 0);
        }

        public void Dispose()
        {
            GCNotifier.UnregisterHandler(OnGcPause);
        }
    }

    public static class GCNotifier
    {
        private static event Action<long>? GcPaused;
        private static long _lastNotification;

        static GCNotifier()
        {
            new Timer(CheckGCPause, null, 0, 10);
        }

        private static void CheckGCPause(object? state)
        {
            var current = GC.GetTotalPauseDuration().Milliseconds;
            if (current > _lastNotification)
            {
                var delta = current - _lastNotification;
                GcPaused?.Invoke(delta);
                _lastNotification = current;
            }
        }

        public static void RegisterHandler(Action<long> handler) => GcPaused += handler;
        public static void UnregisterHandler(Action<long> handler) => GcPaused -= handler;
    }

    public class GCEventListener : EventListener
    {
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name.Equals("Microsoft-Windows-DotNETRuntime"))
            {
                EnableEvents(eventSource, EventLevel.Informational, (EventKeywords)0x1C000080014);
            }
        }
        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            // Просто читаем что угодно — важно держать EventPipe активным
            if (eventData.EventName == "GCStart")
            {
                // можно логировать, если надо
                // Console.WriteLine($"GCStart: {eventData.Payload?[0]}");
            }
        }
    }
}
