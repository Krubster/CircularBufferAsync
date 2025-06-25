using System.Text.Json;

namespace NETwork.LoadProfiles
{
    public class LoadProfile
    {
        public int SimulateSendLatencyMs { get; set; } = 50;
        public int SendJitterMs { get; set; } = 5;
        public int SimulateReadLatencyMs { get; set; } = 30;
        public int ReadJitterMs { get; set; } = 10;
        // public double DropRate { get; set; } = 0.0; // 0.1 = 10% потерь
        public int LowEnd { get; set; } = 20;
        public int HighEnd { get; set; } = 150;
        public Func<int> PacketSizeGenerator { get; set; } = () => Random.Shared.Next(20, 50);
        public int Connections { get; set; } = 1;
        public int TotalBytesToSend { get; set; } = 1024 * 1024 * 100;
        public bool InfiniteTraffic { get; set; } = false;
        public TimeSpan? LoopDelay { get; set; } = null;

        public IReplayProfile? Replay { get; set; } = null;

        public static LoadProfile FromHistogram(Dictionary<int, int> histogram)
        {
            var profile = new LoadProfile();
            var statGen = new StatisticalProfile(histogram);
            profile.PacketSizeGenerator = statGen.NextPacketSize;
            return profile;
        }

        public static LoadProfile FromHistogramFile(string path)
        {
            var json = File.ReadAllText(path);
            var histogram = JsonSerializer.Deserialize<Dictionary<int, int>>(json);
            return FromHistogram(histogram);
        }
    }
}
