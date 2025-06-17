using NETwork;

namespace Framework.BackPressureStrategies
{
    public interface IThreadStats
    {
        public long NetThreadActiveTicks { get; }
        public long LogicThreadActiveTicks { get; }
    }
    public class ThreadUtilizationBackPressure : IBackPressureStrategy
    {
        public Action<ConnectionStats>? OnUpdateMetrics { get; set; }
        private readonly IThreadStats _stats;
        private const long MinTickThreshold = 1_000_000; // минимальное значение тиков, чтобы стратегия начала работать
        private const float LowerBound = 0.7f;
        private const float UpperBound = 1.5f;
        public static Func<bool>? IsColdLogicOverloaded;

        public ThreadUtilizationBackPressure(IThreadStats stats)
        {
            _stats = stats;
        }

        public bool ShouldPauseRecv(NetState state)
        {
            if (IsColdLogicOverloaded?.Invoke() == true)
                return true;

            var netTicks = _stats.NetThreadActiveTicks;
            var logicTicks = _stats.LogicThreadActiveTicks;

            if (logicTicks < MinTickThreshold || netTicks < MinTickThreshold)
                return false;

            float ratio = netTicks / (float)logicTicks;

            if (logicTicks < MinTickThreshold * 2 && netTicks > logicTicks * 10)
                return false;
          //  if (ratio < LowerBound)
          //      return false;

            return ratio > UpperBound;
        }

        public bool ShouldPauseLogic(NetState state)
        {
            if (IsColdLogicOverloaded?.Invoke() == true)
                return false;

            var netTicks = _stats.NetThreadActiveTicks;
            var logicTicks = _stats.LogicThreadActiveTicks;

            if (logicTicks < MinTickThreshold || netTicks < MinTickThreshold)
                return false;

            float ratio = logicTicks / (float)netTicks;

            if (netTicks < MinTickThreshold * 2 && logicTicks > netTicks * 10)
                return false;

           // if (ratio < LowerBound)
            //    return false;

            return ratio > UpperBound;
        }

        public void OnReceive(NetState state, int bytes) { }
        public void OnProcess(NetState state, int bytes) { }
        public void OnSend(NetState state, int bytes) { }

        public void Update(double logicMs)
        {
            throw new NotImplementedException();
        }
    }
}
