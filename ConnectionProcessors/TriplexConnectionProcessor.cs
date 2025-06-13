using CircularBufferAsync;
using CircularBufferAsync.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Net.Sockets;
using System.Network;
using System.Runtime.InteropServices;

namespace Test.ConnectionProcessors
{
    public class TriplexConnectionProcessor : IConnectionProcessor
    {
        private readonly IPacketWorkload _workload;
        private readonly IStatsCollector? _collector;

        private readonly ConcurrentQueue<Socket> _newConnections = new();
        private readonly ConcurrentDictionary<Socket, NetState> _connections = new();

        private long _started, _stopped;
        private static int _bytesReceived, _bytesSent, _packets;

        private CancellationToken _ct;
        private Thread? _netThread, _logicThread, _sendThread;

        private readonly IPollGroup _pollGroup = PollGroup.Create();
        private readonly GCHandle[] _polledStates = new GCHandle[1024];
        private readonly ConcurrentDictionary<int, INetworkBuffer> _inBuffers = new();
       // private readonly ConcurrentDictionary<int, INetworkBuffer> _outBuffers = new();
        private readonly ConcurrentDictionary<int, NetState> _statesById = new();
        private readonly ConcurrentQueue<int> _pendingRead = new();
        private readonly ConcurrentQueue<int> _pendingWrite = new();
        private readonly int[] _pendingPackets = new int[8192];
        private readonly int _maxPendingPackets = 8; // Максимальное количество ожидающих пакетов на соединение
       
        private readonly int[] _pendingWritePackets = new int[8192];
        private readonly int _maxPendingWritePackets = 8; // Максимальное количество ожидающих пакетов на отправку

        private Func<INetworkBuffer> _inFactory;
        private Func<INetworkBuffer> _outFactory;

        public ConcurrentQueue<NetState> FlushPending => _flushPending;
        private readonly ConcurrentQueue<NetState> _flushPending = new();
        private readonly CpuCycleMeter _netMeter = new();
        private readonly CpuCycleMeter _logicMeter = new();
        private readonly CpuCycleMeter _writeMeter = new();

        private static ThreadLocal<Stopwatch> _threadTimer = new(() => Stopwatch.StartNew());
        private static long _logicThreadActiveTicks = 0, _netThreadActiveTicks = 0, _writeThreadActiveTicks = 0;
        public TriplexConnectionProcessor(
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

            _netThread = new Thread(NetworkLoop) { IsBackground = true };
            _logicThread = new Thread(LogicLoop) { IsBackground = true };
            _sendThread = new Thread(SendLoop) { IsBackground = true };

            _netThread.Start();
            _logicThread.Start();
            _sendThread.Start();
        }

        private void NetworkLoop()
        {
            while (!_ct.IsCancellationRequested)
            {
                var localTimer = _threadTimer.Value!;
                long start = localTimer.ElapsedTicks;

                while (_newConnections.TryDequeue(out var sock))
                {
                    var state = new NetState(sock, this);
                    _connections.TryAdd(sock, state);
                    _statesById.TryAdd(state.Id, state);
                    _pollGroup.Add(sock, state.Handle);
                    _inBuffers.TryAdd(state.Id, _inFactory());
                   // _outBuffers.TryAdd(state.Id, _outFactory());
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
                long elapsed = localTimer.ElapsedTicks - start;
                _netThreadActiveTicks += elapsed;
                _netMeter.Tick();
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
                            if (_pendingWritePackets[state.Id] >= _maxPendingWritePackets)
                                continue;
                            _pendingWritePackets[state.Id]++;

                            while (buf.TryPeek(out var data))
                            {
                                var (processed, written, response) = _workload.ProcessBuffer(data, state);
                                _pendingPackets[id]--;
                                _packets += processed;
                                buf.Advance(data.Length); // Увеличиваем указатель чтения в буфере
                                if (response is { } owner)
                                {
                                    // _outBuffers[id].Write(owner.Span, written);
                                    state.Send(owner.Span, written);
                                    _pendingWrite.Enqueue(id);
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

        private void SendLoop()
        {
            while (!_ct.IsCancellationRequested)
            {
                var localTimer = _threadTimer.Value!;
                long start = localTimer.ElapsedTicks;
                FlushSends();
                long elapsed = localTimer.ElapsedTicks - start;
                _writeThreadActiveTicks += elapsed;
                _writeMeter.Tick();
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

            long total = _netThreadActiveTicks + _logicThreadActiveTicks + _writeThreadActiveTicks;
            long netPercent = _netThreadActiveTicks * 100 / total;
            long logicPercent = _logicThreadActiveTicks * 100 / total;
            long writePercent = _writeThreadActiveTicks * 100 / total;

            _collector?.Report(new ConnectionStats
            {
                StartedTicks = _started,
                UptimeTicks = uptime,
                BytesReceived = _bytesReceived,
                BytesSent = _bytesSent,
                PacketsProcessed = _packets,
                ThreadTicks = new long[] {  netPercent, logicPercent, writePercent, 0 },
                Cycles = new[] { _logicMeter.AverageCyclesPerSecond, _netMeter.AverageCyclesPerSecond, _writeMeter.AverageCyclesPerSecond }
            });
        }

        public void Stop()
        {
            _stopped = Environment.TickCount64;
            _ct = new CancellationToken(true); // Принудительно отменяем
            _netThread?.Join();
            _sendThread?.Join();
            _logicThread?.Join();
            ReportStats();
        }
    }
}
