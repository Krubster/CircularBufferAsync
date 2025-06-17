namespace NETwork
{
    public interface IStatsCollector
    {
        void Report(ConnectionStats stats);
        void FlushToFile();
    }
}
