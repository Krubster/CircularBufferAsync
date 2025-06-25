using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Framework.Metrics
{
    public class ContentionMetricsService
    {
        private long _contentionCount;
        private long _contentionTimeTicks;

        public void RecordContention(long ticks)
        {
            Interlocked.Increment(ref _contentionCount);
            Interlocked.Add(ref _contentionTimeTicks, ticks);
        }

        public (long Count, long Ticks) GetAndReset()
        {
            return (
                Interlocked.Exchange(ref _contentionCount, 0),
                Interlocked.Exchange(ref _contentionTimeTicks, 0)
            );
        }
    }
}
