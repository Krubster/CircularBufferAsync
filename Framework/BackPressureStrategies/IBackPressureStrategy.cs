using NETwork;

namespace Framework.BackPressureStrategies
{
    public interface IBackPressureStrategy
    {
        public Action<ConnectionStats>? OnUpdateMetrics { get; set; }
        void OnReceive(NetState state, int bytes);
        void OnProcess(NetState state, int bytes);
        void OnSend(NetState state, int bytes);
        public void Update(double logicMs);
        bool ShouldPauseRecv(NetState state);
        bool ShouldPauseLogic(NetState state);
    }
}
