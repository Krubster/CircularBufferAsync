using System.Collections.Concurrent;
using System.Net.Sockets;

namespace NETwork.ConnectionProcessors
{
    public interface IConnectionProcessor
    {
        void Add(Socket socket); // Called by TcpWorkloadServer
        void Start(CancellationToken token); // Starts worker threads / loops
        void Stop();
        ConcurrentQueue<NetState> FlushPending { get; }
    }
}
