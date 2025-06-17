using NETwork;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Framework.StatsCollectors
{
    public class GraphingStatsCollector : IStatsCollector
    {
        private readonly List<(DateTime Time, ConnectionStats Stats)> _records = new();
        private readonly Timer _flushTimer;
        private readonly string _logFilePath;

        public GraphingStatsCollector(string logFilePath = "stats_log.csv")
        {
            _logFilePath = logFilePath;
        }
        private ConnectionStats? _lastStats = null;

        public void Report(ConnectionStats stats)
        {
            var now = DateTime.UtcNow;
            var deltaStats = new Dictionary<string, double>();

            if (_lastStats != null)
            {
                double seconds = (stats.Uptime - _lastStats.Uptime).TotalSeconds;

                deltaStats["recvRateBps"] = (stats.BytesReceived - _lastStats.BytesReceived) / seconds;
                deltaStats["sendRateBps"] = (stats.BytesSent - _lastStats.BytesSent) / seconds;
                deltaStats["pps"] = (stats.PacketsProcessed - _lastStats.PacketsProcessed) / seconds;
            }

            foreach (var kv in deltaStats)
                stats.AddMetric(kv.Key, kv.Value);

            _lastStats = Clone(stats); // сохранить копию

            _records.Add((now, stats));
        }

        private ConnectionStats Clone(ConnectionStats source)
        {
            var clone = new ConnectionStats
            {
                BytesReceived = source.BytesReceived,
                BytesSent = source.BytesSent,
                PacketsProcessed = source.PacketsProcessed,
                StartedTicks = source.StartedTicks,
                UptimeTicks = source.UptimeTicks,
                ThreadTicks = (long[])source.ThreadTicks.Clone(),
                Cycles = (double[])source.Cycles.Clone()
            };

            foreach (var kvp in source.GetAllMetrics())
                clone.AddMetric(kvp.Key, kvp.Value);

            return clone;
        }

        public void FlushToFile()
        {
            List<(DateTime Time, ConnectionStats Stats)> snapshot;

            if (_records.Count == 0) return;
            snapshot = new(_records);
            _records.Clear();
            string separator = "\t";
            var sb = new StringBuilder();
            var headers = new List<string>
        {
            "Timestamp", "UptimeSeconds", "BytesReceived", "BytesSent", "PacketsProcessed",
            "ThreadNet%", "ThreadLogic%", "CyclesNet", "CyclesLogic"
        };

            // find all unique extended metric names
            var allMetrics = new HashSet<string>();
            foreach (var (_, s) in snapshot)
                foreach (var m in s.GetAllMetrics())
                    allMetrics.Add(m.Key);

            headers.AddRange(allMetrics);

            if (!File.Exists(_logFilePath))
            {
                File.WriteAllText(_logFilePath, string.Join(separator, headers) + "\n");
            }

            foreach (var (time, stats) in snapshot)
            {
                var row = new List<string>
            {
                time.ToString("o"),
                stats.Uptime.TotalSeconds.ToString("F2"),
                stats.BytesReceived.ToString(),
                stats.BytesSent.ToString(),
                stats.PacketsProcessed.ToString(),
                stats.ThreadTicks[0].ToString(),
                stats.ThreadTicks[1].ToString(),
                stats.Cycles[0].ToString("F2"),
                stats.Cycles[1].ToString("F2")
            };

                foreach (var name in allMetrics)
                {
                    if (stats.GetMetric(name) is double val)
                    {
                        if (double.IsNaN(val) || double.IsInfinity(val))
                            row.Add("0.0000"); // или "" если хочешь пропускать
                        else
                            row.Add(val.ToString("F4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        row.Add("");
                    }
                }

                sb.AppendLine(string.Join(separator, row));
            }

            File.AppendAllText(_logFilePath, sb.ToString());
        }
    }
}
