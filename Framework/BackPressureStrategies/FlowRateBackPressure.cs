using NETwork;

namespace Framework.BackPressureStrategies
{
    public class FlowRateBackPressure : IBackPressureStrategy
    {
        public Action<ConnectionStats>? OnUpdateMetrics { get; set; }
        private readonly int[] _recvBuffer = new int[8192];
        private readonly int[] _sendBuffer = new int[8192];
        private readonly int _recvThreshold;
        private readonly int _sendThreshold;

        public FlowRateBackPressure(int recvThreshold = 128 * 1024, int sendThreshold = 64 * 1024)
        {
            _recvThreshold = recvThreshold;
            _sendThreshold = sendThreshold;
        }

        public void OnReceive(NetState state, int bytes) => _recvBuffer[state.Id] += bytes;
        public void OnProcess(NetState state, int bytes)
        {
            _recvBuffer[state.Id] = Math.Max(0, _recvBuffer[state.Id] - bytes);
            _sendBuffer[state.Id] += bytes;
        }
        public void OnSend(NetState state, int bytes) => _sendBuffer[state.Id] = Math.Max(0, _sendBuffer[state.Id] - bytes);

        public bool ShouldPauseRecv(NetState state) => _recvBuffer[state.Id] > _recvThreshold;
        public bool ShouldPauseLogic(NetState state) => _sendBuffer[state.Id] > _sendThreshold;

        public void Update(double logicMs)
        {
            throw new NotImplementedException();
        }

        public bool ShouldPauseNet()
        {
            throw new NotImplementedException();
        }

        public void OnDispose(NetState state)
        {
            _recvBuffer[state.Id] = 0;
            _sendBuffer[state.Id] = 0;
        }
    }
}
