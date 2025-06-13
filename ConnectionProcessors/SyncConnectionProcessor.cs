using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Network;
using System.Runtime.InteropServices;
using CircularBufferAsync;

namespace Test.ConnectionProcessors
{
    public class SyncConnectionProcessor : IConnectionProcessor
    {
        public ConcurrentQueue<NetState> FlushPending => _flushPending;
        private readonly ConcurrentQueue<NetState> _flushPending = new();

        private readonly IPacketWorkload _workload;

        private readonly IStatsCollector? _collector;

        private readonly ConcurrentQueue<Socket> _pending = new();
        private readonly ConcurrentDictionary<Socket, NetState> _states = new();
        private readonly GCHandle[] _polledStates = new GCHandle[2048];
        private readonly IPollGroup _pollGroup = PollGroup.Create();

        private Thread? _worker;
        private CancellationToken _ct;

        private long _started, _stopped;

        private static int _bytesReceived, _bytesSent, _packets;
        private readonly CpuCycleMeter _mainMeter = new();

        public SyncConnectionProcessor(IPacketWorkload workload, IStatsCollector? collector = null)
        {
            _workload = workload;
            _collector = collector;
        }

        public void Add(Socket socket)
        {
            socket.NoDelay = true;
            _pending.Enqueue(socket);
        }

        public void Start(CancellationToken ct)
        {
            _ct = ct;
            _started = Environment.TickCount64;
            _worker = new Thread(ProcessLoop) { IsBackground = true };
            _worker.Start();
        }

        private void ProcessLoop()
        {
            while (!_ct.IsCancellationRequested)
            {
                while (_pending.TryDequeue(out var sock))
                {
                    var state = new NetState(sock, this);
                    _states.TryAdd(sock, state);
                    _pollGroup.Add(sock, state.Handle);
                }

                int count = _pollGroup.Poll(_polledStates);
                for (int i = 0; i < count; i++)
                {
                    if (_polledStates[i].Target is not NetState state)
                        continue;

                    try
                    {
                        _bytesReceived += state.HandleReceive((payload, conn) =>
                        {
                            var (processed, written, response) = _workload.ProcessBuffer(payload, state);
                            _packets += processed;
                            if (response is { } owner)
                            {
                                conn.Send(owner.Span, written);
                                owner.Dispose(); // Освобождаем арендуемый буфер после записи
                            }
                        });
                    }
                    catch
                    {
                        Cleanup(state);
                    }
                }

                FlushSends();
                _mainMeter.Tick();
            }
        }

        private void FlushSends()
        {
            while (_flushPending.TryDequeue(out var state))
            {
                var reader = state.SendPipe.Reader;
                var span = reader.AvailableToRead();
                if (span.Length == 0) continue;

                try
                {
                    int sent = state.Socket.Send(span, SocketFlags.None);
                    reader.Advance((uint)sent);
                    state.FlushReset();
                    _bytesSent += sent;
                }
                catch
                {
                    Cleanup(state);
                }
            }
        }

        private void Cleanup(NetState state)
        {
            _pollGroup.Remove(state.Socket, state.Handle);
            _states.TryRemove(state.Socket, out _);
            state.Dispose();
        }

        private void ReportStats()
        {
            long uptime = (_stopped > 0 ? _stopped : Environment.TickCount64) - _started;
            _collector?.Report(new ConnectionStats
            {
                StartedTicks = _started,
                UptimeTicks = uptime,
                BytesReceived = _bytesReceived,
                BytesSent = _bytesSent,
                PacketsProcessed = _packets,
                Cycles = new []{ _mainMeter.AverageCyclesPerSecond, 0, 0 }
            });
        }

        public void Stop()
        {
            _stopped = Environment.TickCount64;
            _ct = new CancellationToken(true); // Принудительно отменяем
            _worker?.Join();
            ReportStats();
        }
    }
}
