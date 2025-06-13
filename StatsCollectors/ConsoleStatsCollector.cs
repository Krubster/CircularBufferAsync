namespace Test.StatsCollectors
{
    public class ConsoleStatsCollector : IStatsCollector
    {
        public void Report(ConnectionStats stats)
        {
            stats.Log();
        }
    }
}
