using System.Text;
using Test;

namespace CircularBufferAsync.StatsCollectors
{
    public class FileStatsCollector : IStatsCollector
    {
        private readonly string _filePath;
        private readonly object _lock = new();
        private readonly string _workload;
        private readonly string _tag;
        public FileStatsCollector(string filePath, string tag, string workload)
        {
            _filePath = filePath;
            _tag = tag;
            _workload = workload;
        }

        public void Report(ConnectionStats stats)
        {
            var line = string.Join(",",
                DateTime.UtcNow.ToString("o"),
                _tag,
                _workload,
                stats.BytesReceived,
                stats.BytesSent,
                stats.PacketsProcessed,
                ((int)stats.Uptime.TotalSeconds).ToString(),
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2),
                stats.ThreadTicks[0],
                stats.ThreadTicks[1],
                stats.ThreadTicks[2],
                (int)stats.Cycles[0],
                (int)stats.Cycles[1],
                (int)stats.Cycles[2]
            );

            lock (_lock)
            {
                File.AppendAllText(_filePath, line + Environment.NewLine, Encoding.UTF8);
            }

        }
    }
}
