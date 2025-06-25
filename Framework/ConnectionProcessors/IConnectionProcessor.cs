using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading.Channels;

namespace NETwork.ConnectionProcessors
{
    public interface IConnectionProcessor
    {
        void Add(Socket socket); // Called by TcpWorkloadServer
        void Start(CancellationToken token); // Starts worker threads / loops
        void Stop();

        void SetRuntime(string key, string value);
        Channel<NetState> FlushPending { get; }
    }
}
