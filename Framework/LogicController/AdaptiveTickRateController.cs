using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Framework.LogicController
{
    public class AdaptiveTickRateController : IColdLogicController
    {
        private readonly int _minTickMs;
        private readonly int _maxTickMs;
        private readonly Func<bool> _isColdLogicOverloaded;

        public AdaptiveTickRateController(int minTickMs = 20, int maxTickMs = 100, Func<bool>? isOverloaded = null)
        {
            _minTickMs = minTickMs;
            _maxTickMs = maxTickMs;
            _isColdLogicOverloaded = isOverloaded ?? (() => false);
        }

        public bool ShouldRunTick(long elapsedSinceLastTickMs)
        {
            return elapsedSinceLastTickMs >= GetTargetTickDurationMs();
        }

        public int GetTargetTickDurationMs()
        {
            return _isColdLogicOverloaded() ? _maxTickMs : _minTickMs;
        }
    }
}
