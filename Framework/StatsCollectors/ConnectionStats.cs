
using System.Text.Json;

namespace NETwork
{
    public class ConnectionStats
    {
        public Dictionary<string, double> Metrics => _metrics;

        private readonly Dictionary<string, double> _metrics = new();

        public void AddMetric(string key, double value)
        {
            _metrics[key] = value;
        }

        public double? GetMetric(string key) =>
            _metrics.TryGetValue(key, out var value) ? value : null;

        public IEnumerable<KeyValuePair<string, double>> GetAllMetrics() => _metrics;

        public ConnectionStats Clone()
        {
            ConnectionStats newStats = new ConnectionStats();
            foreach (var (k, val) in _metrics)
            {
                newStats.AddMetric(k, val);
            }

            return newStats;
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }

        public static ConnectionStats? FromJson(string json)
        {
            return JsonSerializer.Deserialize<ConnectionStats>(json);
        }
    }
}
