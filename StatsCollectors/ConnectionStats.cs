namespace Test
{
    public class ConnectionStats
    {
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
