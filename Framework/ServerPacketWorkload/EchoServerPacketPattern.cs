using NETwork;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Framework.Workloads
{
    public class EchoServerPacketPattern : IServerTrafficPattern
    {
        private static readonly Random _random = new Random();

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

        public EchoServerPacketPattern(int loweEnd = 40, int highEnd = 250)
        {
            _lowEnd = loweEnd;
            _highEnd = highEnd;
        }


        public int? GetNextPayloadLength(NetState state)
        {
            return _random.Next(_lowEnd, _highEnd);
        }
    }
}
