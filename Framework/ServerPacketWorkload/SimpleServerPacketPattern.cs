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
        public int? GetNextPayloadLength(NetState state)
        {
            if (_random.NextDouble() < 0.3)
            {
                return _random.Next(40, 250);
            }

            return null;
        }
    }
}
