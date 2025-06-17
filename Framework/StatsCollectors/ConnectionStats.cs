namespace NETwork
{
    public class ConnectionStats
    {
        public Dictionary<string, double> ExtendedMetrics { get; } = new();

        public void AddMetric(string key, double value)
        {
            ExtendedMetrics[key] = value;
        }

        public double? GetMetric(string key)
        {
            return ExtendedMetrics.TryGetValue(key, out var value) ? value : null;
        }

        public IEnumerable<KeyValuePair<string, double>> GetAllMetrics()
        {
            return ExtendedMetrics;
        }

        public long BytesReceived;
        public long BytesSent;
        public long PacketsProcessed;
        public long StartedTicks = Environment.TickCount64;
        public long UptimeTicks;
        public long[] ThreadTicks = new long[3];
        public double[] Cycles = new double[3];

        public TimeSpan Uptime => TimeSpan.FromMilliseconds(UptimeTicks);

        public void Log(string tag = "")
        {
            Console.WriteLine($"[Stats{(string.IsNullOrEmpty(tag) ? "" : $"::{tag}")}] " +
                              $"Recv: {BytesReceived} B | Sent: {BytesSent} B | " +
                              $"Packets: {PacketsProcessed} | " +
                              $"Uptime: {Uptime.TotalSeconds:F1}s | " +
                              $"GC0: {GC.CollectionCount(0)} GC1: {GC.CollectionCount(1)} GC2: {GC.CollectionCount(2)}");
        }
    }
}
