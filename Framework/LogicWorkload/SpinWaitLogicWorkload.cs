using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NETwork;

namespace Framework.Workloads
{
    public class SpinWaitLogicWorkload : ILogicWorkload
    {
        private int _cpuWork;
        public SpinWaitLogicWorkload(int cpuWork = 1000)
        {
            _cpuWork = cpuWork;
        }

        public void Execute()
        {
            // Just spin cpu thread
            Thread.SpinWait(_cpuWork);
        }
    }
}
