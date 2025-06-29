using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace System.Network.EPoll;

internal sealed class EPollGroup<TArch, TEvent> : IPollGroup
    where TArch : IArch<TEvent>
    where TEvent : struct, IEpollEvent
{
    private readonly nint _epHndle;
    private TEvent[] _events;

    public EPollGroup()
    {
        _events = new TEvent[2048];
        _epHndle = TArch.epoll_create1(epoll_flags.NONE);

        if (_epHndle == 0)
        {
            throw new Exception("Unable to initialize poll group");
        }
    }

    public void Add(Socket socket, GCHandle handle)
    {
        var ev = new TEvent
        {
            Events = epoll_events.EPOLLIN | epoll_events.EPOLLERR,
            Ptr = (nint)handle
        };

        TArch.epoll_ctl(_epHndle, epoll_op.EPOLL_CTL_ADD, socket.Handle, ref ev);
    }
    public int Poll(int maxEvents, int timeout)
    {
        if (maxEvents > _events.Length)
        {
            var newLength = Math.Max(maxEvents, _events.Length + (_events.Length >> 2));
            _events = new TEvent[newLength];
        }

        return TArch.epoll_wait(_epHndle, _events, maxEvents, timeout);
    }

    public int Poll(int maxEvents)
    {
        return Poll(maxEvents, 0);
    }
    public int Poll(nint[] ptrs)
    {
        return Poll(ptrs.Length, 0);
    }

    public int Poll(nint[] ptrs, int timeout)
    {
        var rc = Poll(ptrs.Length, timeout);

        if (rc <= 0)
        {
            return rc;
        }

        for (var i = 0; i < rc; i++)
        {
            ptrs[i] = _events[i].Ptr;
        }

        return rc;
    }
    public int Poll(GCHandle[] handles)
    {
        return Poll(handles, 0);
    }

    public int Poll(GCHandle[] handles, int timeout)
    {
        var rc = Poll(handles.Length, timeout);

        if (rc <= 0)
        {
            return rc;
        }

        for (var i = 0; i < rc; i++)
        {
            handles[i] = (GCHandle)_events[i].Ptr;
        }

        return rc;
    }

    public void Remove(Socket socket, GCHandle handle)
    {
        var ev = new TEvent
        {
            Events = epoll_events.EPOLLIN | epoll_events.EPOLLERR,
            Ptr = (nint)handle
        };

        int rc = TArch.epoll_ctl(_epHndle, epoll_op.EPOLL_CTL_DEL, socket.Handle, ref ev);

        if (rc != 0)
        {
            throw new Exception($"epoll_ctl failed with error code {Marshal.GetLastWin32Error()}");
        }
    }

    public void Dispose()
    {
        TArch.epoll_close(_epHndle);
    }
}
