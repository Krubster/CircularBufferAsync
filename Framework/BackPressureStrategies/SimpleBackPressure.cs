using NETwork;

namespace Framework.BackPressureStrategies
{
    public class SimpleBackPressure : IBackPressureStrategy
    {
        public Action<ConnectionStats>? OnUpdateMetrics { get; set; }
        private readonly int[] _unprocessedBytes = new int[8192];
        private readonly int _threshold;

        public SimpleBackPressure(int threshold = 16384)
        {
            _threshold = threshold;
        }

        public void OnDispose(NetState state)
        {
            _unprocessedBytes[state.Id] = 0;
        }

        public void OnReceive(NetState state, int bytes) => _unprocessedBytes[state.Id] += bytes;

        public void OnProcess(NetState state, int bytes) => _unprocessedBytes[state.Id] = Math.Max(0, _unprocessedBytes[state.Id] - bytes);

        public void OnSend(NetState state, int bytes)
        {
        }

        public bool ShouldPauseRecv(NetState state)
        {
            return _unprocessedBytes[state.Id] > _threshold;
        }

        public bool ShouldPauseLogic(NetState state)
        {
            return false;
        }

        public void Update(double logicMs)
        {
            throw new NotImplementedException();
        }

        public bool ShouldPauseNet()
        {
            throw new NotImplementedException();
        }
    }
}
