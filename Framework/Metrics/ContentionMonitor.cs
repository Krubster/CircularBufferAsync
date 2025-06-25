using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Framework.Metrics
{
    /* Usage:
    using (new ContentionMonitor())
    {
        // critical section
    }
 */
    public class ContentionMonitor : IDisposable
    {
        private readonly object _lockObject; // Добавлено
        private readonly Stopwatch _sw = Stopwatch.StartNew();

        public ContentionMonitor(object lockObject) // Исправлено
        {
            _lockObject = lockObject;
            Monitor.Enter(_lockObject);
        }

        public void Dispose()
        {
            Monitor.Exit(_lockObject);
            var elapsed = _sw.ElapsedTicks;
            TrackerServices.ContentionMetrics.RecordContention(elapsed); // Исправлено
        }
    }
}

