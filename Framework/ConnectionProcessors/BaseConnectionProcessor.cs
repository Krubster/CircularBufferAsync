using NETwork.ConnectionProcessors;
using NETwork;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Network;
using System.Runtime.InteropServices;
using Framework.Workloads;

namespace Framework.ConnectionProcessors
{
    public abstract class BaseConnectionProcessor : IConnectionProcessor
    {
        public ConcurrentQueue<NetState> FlushPending => _flushPending;
        protected readonly ConcurrentQueue<NetState> _flushPending = new();
        protected readonly ConcurrentQueue<Socket> _newConnections = new();

        protected readonly IPacketProcessor Processor;
        protected readonly IStatsCollector? _collector;
        protected readonly ConcurrentDictionary<Socket, NetState> _states = new();
        protected readonly IPollGroup _pollGroup = PollGroup.Create();
        protected readonly GCHandle[] _polledStates;

        protected CancellationToken _ct;
        protected long _started, _stopped;

        protected int _bytesReceived, _bytesSent, _packets;
        protected ILogicWorkload _logicWorkload;
        protected IServerTrafficPattern _serverTrafficPattern;
        protected BaseConnectionProcessor(IPacketProcessor processor, IStatsCollector? collector, ILogicWorkload lw, IServerTrafficPattern pattern, int pollSize)
        {
            Processor = processor;
            _collector = collector;
            _logicWorkload = lw;
            _serverTrafficPattern = pattern;
            _polledStates = new GCHandle[pollSize];
        }

        public virtual void Add(Socket socket)
        {
            _newConnections.Enqueue(socket);
            socket.NoDelay = true;
        }

        public abstract void Start(CancellationToken token);

        public virtual void Stop()
        {
            _stopped = Environment.TickCount64;
            _ct = new CancellationToken(true);
            OnStopped();
            _collector?.FlushToFile();
        }

        protected abstract void OnStopped();

        protected virtual void OnSent(NetState state, int sent)
        {
        }

        protected virtual void FlushSends()
        {
            while (_flushPending.TryDequeue(out var state))
            {
                try
                {
                    while (state.SendBuffer.TryPeek(out var buffer))
                    {
                        int sent = state.Socket.Send(buffer, SocketFlags.None);
                        state.FlushReset();
                        _bytesSent += sent;
                        state.SendBuffer.Advance(buffer.Length);
                        OnSent(state, sent);
                    }
                }
                catch
                {
                    Cleanup(state);
                }
            }
        }

        protected virtual void Cleanup(NetState state)
        {
            _pollGroup.Remove(state.Socket, state.Handle);
            _states.TryRemove(state.Socket, out _);
            state.Dispose();
        }

        protected abstract void ReportStats();
    }
}
