using NETwork;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Framework.Workloads
{
    public class SimpleServerPacketPattern : IServerTrafficPattern
    {
        private static readonly Random _random = new Random();
        public double SendChance { get => _chance;
            set
            {
                _chance = value;
            }
        }
        public int HighEnd
        {
            get => _highEnd;
            set
            {
                _highEnd = value;
            }
        }
        public int LowEnd
        {
            get => _lowEnd;
            set
            {
                _lowEnd = value;
            }
        }

        private int _lowEnd, _highEnd;
        private double _chance;

        public SimpleServerPacketPattern(double chance = 0.3, int loweEnd = 40, int highEnd = 250)
        {
            _chance = chance;
            _lowEnd = loweEnd;
            _highEnd = highEnd;
        }


        public int? GetNextPayloadLength(NetState state)
        {
            if (_random.NextDouble() < _chance)
            {
                return _random.Next(_lowEnd, _highEnd);
            }

            return null;
        }
    }
}
