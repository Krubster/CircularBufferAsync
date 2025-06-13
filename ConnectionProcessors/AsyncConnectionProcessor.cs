using CircularBufferAsync;
using CircularBufferAsync.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Network;
using System.Runtime.InteropServices;

namespace Test.ConnectionProcessors
{
    public class AsyncConnectionProcessor : IConnectionProcessor
    {
        public ConcurrentQueue<NetState> FlushPending => _flushPending;
        private readonly ConcurrentQueue<NetState> _flushPending = new();

        private readonly IPacketWorkload _workload;
        private readonly IStatsCollector? _collector;

        private readonly ConcurrentQueue<Socket> _newConnections = new();
        private readonly ConcurrentDictionary<Socket, NetState> _connections = new();
        private readonly IPollGroup _pollGroup = PollGroup.Create();
        private readonly GCHandle[] _polledStates = new GCHandle[1024];
        private readonly ConcurrentDictionary<int, INetworkBuffer> _inBuffers = new();
       // private readonly ConcurrentDictionary<int, INetworkBuffer> _outBuffers = new();
        private readonly ConcurrentDictionary<int, NetState> _statesById = new();
        private readonly ConcurrentQueue<int> _pendingRead = new();
        //private readonly ConcurrentQueue<int> _pendingWrite = new();
        private readonly int[] _pendingPackets = new int[8192];
        private readonly int _maxPendingPackets = 16; // Максимальное количество ожидающих пакетов на соединение
        private long _started, _stopped;

        private static int _bytesReceived, _bytesSent, _packets;

        private CancellationToken _ct;
        private Thread? _netThread, _logicThread;
        private Func<INetworkBuffer> _inFactory;
        private Func<INetworkBuffer> _outFactory;

        private static ThreadLocal<Stopwatch> _threadTimer = new(() => Stopwatch.StartNew());
        private static long _logicThreadActiveTicks = 0, _netThreadActiveTicks = 0;
        private readonly CpuCycleMeter _netMeter = new();
        private readonly CpuCycleMeter _logicMeter = new();

        public AsyncConnectionProcessor(
            Func<INetworkBuffer> inBufferFactory,
            Func<INetworkBuffer> outBufferFactory,
            IPacketWorkload workload,
            IStatsCollector? collector = null)
        {
            _inFactory = inBufferFactory;
            _outFactory = outBufferFactory;
            _workload = workload;
            _collector = collector;
        }

        public void Add(Socket socket)
        {
            socket.NoDelay = true;
            _newConnections.Enqueue(socket);
        }

        public void Start(CancellationToken ct)
        {
            _ct = ct;
            _started = Environment.TickCount64;

            _netThread = new Thread(NetworkLoop);
            _logicThread = new Thread(LogicLoop);
            _netThread.Start();
            _logicThread.Start();
        }

        private void NetworkLoop()
        {
            while (!_ct.IsCancellationRequested)
            {
                var localTimer = _threadTimer.Value!;
                long start = localTimer.ElapsedTicks;
                // Add new sockets
                while (_newConnections.TryDequeue(out var sock))
                {
                    var state = new NetState(sock, this);
                    _connections.TryAdd(sock, state);
                    _statesById.TryAdd(state.Id, state);
                    _pollGroup.Add(sock, state.Handle);
                    _inBuffers.TryAdd(state.Id, _inFactory());
                }

                int count = _pollGroup.Poll(_polledStates);
                for (int i = 0; i < count && !_ct.IsCancellationRequested; i++)
                {
                    if (_polledStates[i].Target is not NetState state)
                        continue;
                    try
                    {
                        if (_pendingPackets[state.Id] >= _maxPendingPackets)
                            continue;
                        _pendingPackets[state.Id]++;
                        _bytesReceived += state.HandleReceive((payload, conn) =>
                        {
                            _inBuffers[conn.Id].Write(payload);
                            _pendingRead.Enqueue(conn.Id);
                        });
                    }
                    catch
                    {
                        Cleanup(state.Socket);
                    }
                }


                /*while (_pendingWrite.TryDequeue(out var id))
                {
                    if (_outBuffers.TryGetValue(id, out var buf))
                    {
                        while (buf.TryPeek(out var data))
                        {
                            if (_statesById.TryGetValue(id, out var conn))
                            {
                                conn.Send(data);
                                buf.Advance(data.Length);
                            }
                        }
                    }
                }*/

                FlushSends();

                long elapsed = localTimer.ElapsedTicks - start;
                _netThreadActiveTicks += elapsed;
                _netMeter.Tick();

            }

        }

        [ThreadStatic]
        private static byte[]? _sendBuffer = new byte[65536];

        private static byte[] GetSendBuffer(int size)
        {
            return _sendBuffer;
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
                    var buffer = GetSendBuffer(span.Length);
                    span.CopyTo(buffer);
                    int sent = state.Socket.Send(buffer, 0, span.Length, SocketFlags.None);
                    reader.Advance((uint)sent);
                    state.FlushReset();
                    _bytesSent += sent;
                }
                catch
                {
                    Cleanup(state.Socket);
                }
            }
        }

        private void LogicLoop()
        {
            while (!_ct.IsCancellationRequested)
            {
                var localTimer = _threadTimer.Value!;
                long start = localTimer.ElapsedTicks;
                while (_pendingRead.TryDequeue(out var id))
                {
                    if (_inBuffers.TryGetValue(id, out var buf))
                    {
                        if (_statesById.TryGetValue(id, out var state))
                        {
                            while (buf.TryPeek(out var data))
                            {
                                var (processed, written, response) = _workload.ProcessBuffer(data, state);
                                _pendingPackets[id]--;
                                _packets += processed;
                                buf.Advance(data.Length); // Увеличиваем указатель чтения в буфере
                                if (response is { } owner)
                                {
                                    state.Send(owner.Span, written);
                                    //_outBuffers[id].Write(owner.Span, written);
                                    //_pendingWrite.Enqueue(id);
                                    owner.Dispose(); // Освобождаем арендуемый буфер после записи
                                }
                            }
                        }
                    }
                }
                long elapsed = localTimer.ElapsedTicks - start;
                _logicThreadActiveTicks += elapsed;
                _logicMeter.Tick();
            }
        }

        private void Cleanup(Socket sock)
        {
            _connections.TryRemove(sock, out var state);
            if (state != null)
            {
                _statesById.TryRemove(state.Id, out _);
                state.Dispose();
            }
        }

        private void ReportStats()
        {
            long uptime = (_stopped > 0 ? _stopped : Environment.TickCount64) - _started;
            long total = _netThreadActiveTicks + _logicThreadActiveTicks + 1;
            long netPercent = _netThreadActiveTicks * 100 / total;
            long logicPercent = _logicThreadActiveTicks * 100 / total;

            _collector?.Report(new ConnectionStats
            {
                StartedTicks = _started,
                UptimeTicks = uptime,
                BytesReceived = _bytesReceived,
                BytesSent = _bytesSent,
                PacketsProcessed = _packets,
                ThreadTicks = new long[]{ netPercent, logicPercent, 0 },
                Cycles = new[] { _logicMeter.AverageCyclesPerSecond, _netMeter.AverageCyclesPerSecond, 0 }
            });
        }

        public void Stop()
        {
            _stopped = Environment.TickCount64;
            _ct = new CancellationToken(true); // Принудительно отменяем
            _netThread?.Join();
            _logicThread?.Join();
            ReportStats();
        }
    }
}
