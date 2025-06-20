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
        private MetricsBroadcastServer? _broadcastServer;
        private ConnectionStats? _lastStats = null;
        public GraphingStatsCollector(string logFilePath = "stats_log.csv")
        {
            _logFilePath = logFilePath;
        }
        public void EnableLiveBroadcast(int port = 12345)
        {
            _broadcastServer = new MetricsBroadcastServer(port);
            _broadcastServer.Start();
        }
        public void Report(ConnectionStats stats)
        {
            var now = DateTime.UtcNow;

            _records.Add((now, stats));
            _broadcastServer?.Broadcast(stats);
        }

        public void FlushToFile()
        {
            if (_records.Count == 0) return;

            var snapshot = new List<(DateTime Time, ConnectionStats Stats)>(_records);
            _records.Clear();

            string separator = "\t";
            var sb = new StringBuilder();

            // Собрать все уникальные имена метрик
            var allKeys = snapshot.SelectMany(s => s.Stats.GetAllMetrics().Select(m => m.Key)).ToHashSet();

            var headers = new List<string> { "Timestamp" };
            headers.AddRange(allKeys);

            if (!File.Exists(_logFilePath))
                File.WriteAllText(_logFilePath, string.Join(separator, headers) + "\n");

            foreach (var (time, stats) in snapshot)
            {
                var row = new List<string> { time.ToString("o") };

                foreach (var key in allKeys)
                {
                    if (stats.GetMetric(key) is double val)
                    {
                        if (double.IsNaN(val) || double.IsInfinity(val))
                            row.Add("0.0000");
                        else
                            row.Add(val.ToString("F4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        row.Add(""); // метрики не было
                    }
                }

                sb.AppendLine(string.Join(separator, row));
            }

            File.AppendAllText(_logFilePath, sb.ToString());
        }
    }
}
