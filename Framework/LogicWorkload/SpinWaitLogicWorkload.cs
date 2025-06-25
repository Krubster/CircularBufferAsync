using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NETwork;

namespace Framework.Workloads
{
    public class SpinWaitLogicWorkload : ILogicWorkload
    {
        public int Workload
        {
            get => _workUs;
            set
            {
                _workUs = value;
            }
        }
        private int _workUs;
        public SpinWaitLogicWorkload(int workUs = 1000)
        {
            _workUs = workUs;
        }

        public void Execute()
        {
            long ticksPerMicrosecond = Stopwatch.Frequency / 1_000_000;
            long targetTicks = _workUs * ticksPerMicrosecond;

            long start = Stopwatch.GetTimestamp();
            while (Stopwatch.GetTimestamp() - start < targetTicks)
            {
                Thread.SpinWait(10); // активное ожидание
            }
        }
    }
}
