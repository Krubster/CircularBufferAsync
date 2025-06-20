using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace System.Network;

public interface IPollGroup : IDisposable
{
    void Add(Socket sock, GCHandle handle);
    void Remove(Socket sock, GCHandle handle);
    int Poll(int maxEvents);
    int Poll(nint[] ptrs);
    int Poll(GCHandle[] handles);
    int Poll(int maxEvents, int timeout);
    int Poll(nint[] ptrs, int timeout);
    int Poll(GCHandle[] handles, int timeout);
}
