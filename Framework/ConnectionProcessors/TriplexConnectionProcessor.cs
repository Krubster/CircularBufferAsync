using Framework.BackPressureStrategies;
using Framework.Buffers;
using Framework.ConnectionProcessors;
using Framework.LogicController;
using Framework.Workloads;
using NETwork;
using NETwork.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;

public class TriplexConnectionProcessor : BaseConnectionProcessor, IThreadStats
{
    public long NetThreadActiveTicks => _netThreadActiveTicks;
    public long LogicThreadActiveTicks => _logicThreadActiveTicks;

    private readonly ConcurrentDictionary<int, NetState> _statesById = new();
    private readonly ConcurrentQueue<int> _pendingRead = new();

    private Thread? _netThread, _logicThread, _sendThread, _reportUpdater;
    private Func<INetworkBuffer> _inFactory;
    private Func<INetworkBuffer> _outFactory;

    private static ThreadLocal<Stopwatch> _threadTimer = new(() => Stopwatch.StartNew());
    private static long _logicThreadActiveTicks = 0, _netThreadActiveTicks = 0, _writeThreadActiveTicks = 0;
    private readonly CpuCycleMeter _netMeter = new();
    private readonly CpuCycleMeter _logicMeter = new();
    private readonly CpuCycleMeter _writeMeter = new();
    private IBackPressureStrategy _backPressure;

    private readonly Stopwatch _logicStopwatch = Stopwatch.StartNew();
    private long _lastColdTickTimeMs = 0;
    private readonly IColdLogicController _coldController;

    public TriplexConnectionProcessor(Func<INetworkBuffer> inBufferFactory, Func<INetworkBuffer> outBufferFactory, IPacketProcessor processor, ILogicWorkload logicWorkload, IServerTrafficPattern pattern, IStatsCollector? collector = null)
        : base(processor, collector, logicWorkload, pattern, 1024)
    {
        _coldController = new AdaptiveTickRateController(20, 100, () => false);
        _backPressure = new ThreadPidBackPressure(this, targetLogicMs: 20);
        _inFactory = inBufferFactory;
        _outFactory = outBufferFactory;
    }

    public override void Start(CancellationToken ct)
    {
        _ct = ct;
        _started = Environment.TickCount64;
        _netThread = new Thread(NetworkLoop) { IsBackground = true, Name = "NetReadThread" };
        _logicThread = new Thread(LogicLoop) { IsBackground = true, Name = "LogicThread" };
        _sendThread = new Thread(SendLoop) { IsBackground = true, Name = "NetWriteThread" };
        _reportUpdater = new Thread(() => {
            while (!_ct.IsCancellationRequested)
            {
                ReportStats();
                Thread.Sleep(10);
            }
        })
        { IsBackground = true, Name = "BackPressureUpdater" };
        _netThread.Start();
        _logicThread.Start();
        _sendThread.Start();
        _reportUpdater.Start();
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
                _states.TryAdd(sock, state);
                _statesById.TryAdd(state.Id, state);
                _pollGroup.Add(sock, state.Handle);
                state.RecvBuffer = _inFactory();
                state.SendBuffer = _outFactory();
            }

            int count = _pollGroup.Poll(_polledStates);
            for (int i = 0; i < count && !_ct.IsCancellationRequested; i++)
            {
                if (_polledStates[i].Target is not NetState state) continue;

                if (_backPressure.ShouldPauseRecv(state))
                    continue;

                try
                {
                    _bytesReceived += state.HandleReceive((payload, conn) =>
                    {
                        _backPressure.OnReceive(conn, payload.Length);
                        conn.RecvBuffer.Write(payload);
                        _pendingRead.Enqueue(conn.Id);
                    });
                }
                catch { Cleanup(state); }
            }

            _netThreadActiveTicks += localTimer.ElapsedTicks - start;
            _netMeter.Tick();
        }
    }

    private void LogicLoop()
    {
        while (!_ct.IsCancellationRequested)
        {
            var logicSw = Stopwatch.StartNew();
            var localTimer = _threadTimer.Value!;
            long start = localTimer.ElapsedTicks;
            
            while (_pendingRead.TryDequeue(out var id) && !_ct.IsCancellationRequested)
            {
                if (_statesById.TryGetValue(id, out var state))
                {
                    if (_backPressure.ShouldPauseLogic(state))
                        continue;

                    while (state.RecvBuffer.TryPeek(out var data) && !_ct.IsCancellationRequested)
                    {
                        var processed = Processor.ProcessBuffer(data, state);
                        _backPressure.OnProcess(state, data.Length);
                        _packets += processed;
                        state.RecvBuffer.Advance(data.Length);
                    }

                    int? packetLen = _serverTrafficPattern.GetNextPayloadLength(state);
                    if (packetLen != null)
                    {
                        Span<byte> bufToWrite = state.SendBuffer.GetWriteSpan((int)packetLen + 2, out var commit);
                        SpanWriter writer = new SpanWriter(bufToWrite);
                        writer.Write((ushort)packetLen);
                        for (int k = 0; k < packetLen; ++k)
                            writer.Write((byte)0);
                        commit((int)(packetLen + 2));
                        state.Send(writer.Span, (int)(packetLen + 2));
                    }
                }
            }

            RunColdTickIfDue();
            _backPressure?.Update(logicSw.Elapsed.TotalMilliseconds);

            _logicThreadActiveTicks += localTimer.ElapsedTicks - start;
            _logicMeter.Tick();
        }
    }

    private void RunColdTickIfDue()
    {
        long nowMs = _logicStopwatch.ElapsedMilliseconds;
        long elapsed = nowMs - _lastColdTickTimeMs;

        if (!_coldController.ShouldRunTick(elapsed))
            return;

        _lastColdTickTimeMs = nowMs;
        _logicWorkload.Execute();
    }

    private void SendLoop()
    {
        while (!_ct.IsCancellationRequested)
        {
            var localTimer = _threadTimer.Value!;
            long start = localTimer.ElapsedTicks;
            FlushSends();
            _writeThreadActiveTicks += localTimer.ElapsedTicks - start;
            _writeMeter.Tick();
        }
    }

    protected override void OnSent(NetState state, int sent)
    {
        _backPressure.OnSend(state, sent);
    }

    protected override void OnStopped()
    {
        _netThread?.Join();
        _logicThread?.Join();
        _sendThread?.Join();
        _reportUpdater?.Join();
    }

    protected override void ReportStats()
    {
        long uptime = (_stopped > 0 ? _stopped : Environment.TickCount64) - _started;
        long total = _netThreadActiveTicks + _logicThreadActiveTicks + _writeThreadActiveTicks + 1;
        long netPercent = _netThreadActiveTicks * 100 / total;
        long logicPercent = _logicThreadActiveTicks * 100 / total;
        long writePercent = _writeThreadActiveTicks * 100 / total;

        var stats = new ConnectionStats
        {
            StartedTicks = _started,
            UptimeTicks = uptime,
            BytesReceived = _bytesReceived,
            BytesSent = _bytesSent,
            PacketsProcessed = _packets,
            ThreadTicks = new[] { netPercent, logicPercent, writePercent },
            Cycles = new[] { _logicMeter.AverageCyclesPerSecond, _netMeter.AverageCyclesPerSecond, _writeMeter.AverageCyclesPerSecond }
        };

        _backPressure.OnUpdateMetrics?.Invoke(stats);
        _collector?.Report(stats);
    }
}
