using Framework.ConnectionProcessors;
using NETwork.Buffers;
using NETwork;
using System.Collections.Concurrent;
using System.Diagnostics;
using Framework.BackPressureStrategies;
using Framework.Workloads;
using Framework.Buffers;
using Framework.LogicController;
using System.Net.Sockets;
using Framework.Metrics;

public class AsyncConnectionProcessor : BaseConnectionProcessor, IThreadStats
{
    public long NetThreadActiveTicks => _netThreadActiveTicks;
    public long LogicThreadActiveTicks => _logicThreadActiveTicks;
    public long SendThreadActiveTicks { get; }

    private readonly ConcurrentDictionary<int, NetState> _statesById = new();
    private readonly ConcurrentQueue<int> _pendingRead = new();

    private Thread? _netThread, _logicThread;
    private Func<INetworkBuffer> _inFactory;
    private Func<INetworkBuffer> _outFactory;

    private static ThreadLocal<Stopwatch> _threadTimer = new(() => Stopwatch.StartNew());
    private static long _logicThreadActiveTicks = 0, _netThreadActiveTicks = 0;
    private readonly CpuCycleMeter _netMeter = new();
    private readonly CpuCycleMeter _logicMeter = new();
    
    // Logic thread regulation
    private readonly IColdLogicController _coldController;
    private long _lastColdTickTimeMs = 0;
    private ulong _bytesWritten = 0;
    private readonly Stopwatch _logicStopwatch = Stopwatch.StartNew();

    private IBackPressureStrategy _backPressure;
    private Thread? _reportUpdater;

    private GCMetricsService _gcMetrics = new();
    private ThreadMetricsService _netMetrics = new();
    private ThreadMetricsService _logicMetrics = new();
    private ContentionMetricsService _contentionService = new();

    public AsyncConnectionProcessor(Func<INetworkBuffer> inBufferFactory, Func<INetworkBuffer> outBufferFactory, IPacketProcessor processor, ILogicWorkload logicWorkload, IServerTrafficPattern pattern, IStatsCollector? collector = null)
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
        _started = (ulong)Environment.TickCount64;
        _netThread = new Thread(NetworkLoop) { IsBackground =true, Name = "NetThread"};
        _logicThread = new Thread(LogicLoop) { IsBackground = true, Name = "LogicThread" };
        _reportUpdater = new Thread(() =>
        {
            while (!_ct.IsCancellationRequested)
            {
                ReportStats();
                Thread.Sleep(100);
            }
        })
        {
            IsBackground = true,
            Name = "MetricsThread"
        };
        _netThread.Start();
        _logicThread.Start();
        _reportUpdater.Start();
    }

    private void NetworkLoop()
    {
        ThreadTracker.BindMetrics(_netMetrics);
        while (!_ct.IsCancellationRequested)
        {
            int count = _pollGroup.Poll(_polledStates, 2);
            var localTimer = _threadTimer.Value!;
            long start = localTimer.ElapsedTicks;

            while (_newConnections.Reader.TryRead(out var sock))
            {
                var state = new NetState(sock, this);
                _states.TryAdd(sock, state);
                _statesById.TryAdd(state.Id, state);
                _pollGroup.Add(sock, state.Handle);
                state.RecvBuffer = _inFactory();
                state.SendBuffer = _outFactory();
            }
            for (int i = 0; i < count && !_ct.IsCancellationRequested; i++)
            {
                if (_polledStates[i].Target is not NetState state) continue;

                if (_backPressure.ShouldPauseRecv(state))
                {
                    if (state.SendBuffer.WrittenBytes > 0)
                    {
                        state.FlushSet();
                        try
                        {
                            _flushPending.Writer.TryWrite(state);
                        }
                        catch { }
                    }
                    continue;
                }

                try
                {
                    _bytesReceived += (ulong)state.HandleReceive((payload, conn) =>
                    {
                        _backPressure.OnReceive(conn, payload.Length);

                        LatencyMetrics.MarkPacket(payload);

                        conn.RecvBuffer.Write(payload);
                        _pendingRead.Enqueue(conn.Id);
                    });
                }
                catch { Cleanup(state); }
                _polledStates[i] = default;
            }

            FlushSends();
            _netThreadActiveTicks += localTimer.ElapsedTicks - start;
            _netMeter.Tick();

            if (_netMeter.AverageCyclesPerSecond > 125)
            {
                ThreadTracker.SafeYield();
            }
        }
    }

    private void LogicLoop()
    {
        ThreadTracker.BindMetrics(_logicMetrics);

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
                        var (processed, timestamps) = Processor.ProcessBuffer(data, state);
                        _backPressure.OnProcess(state, data.Length);
                        _packets += (ulong)processed;
                        state.RecvBuffer.Advance(data.Length);
                        // Emulating steady output
                        foreach(uint timestamp in timestamps){
                            int? packetLen2 = _echoTrafficPattern.GetNextPayloadLength(state);
                            if (packetLen2 != null)
                            {
                                Span<byte> bufToWrite =
                                    state.SendBuffer.GetWriteSpan((int)packetLen2 + 2, out var commit);
                                SpanWriter writer = new SpanWriter(bufToWrite);
                                writer.Write((ushort)packetLen2);
                                for (int k = 0; k < packetLen2; ++k)
                                    writer.Write((byte)0);

                                LatencyMetrics.MarkPacket(bufToWrite, timestamp);

                                commit((int)(packetLen2 + 2));
                                state.Send(writer.Span, (int)(packetLen2 + 2));
                                _bytesWritten += (ulong)(packetLen2 + 2);
                            }
                        }
                    }
                    // Emulating background output
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
                        _bytesWritten += (ulong)(packetLen + 2);
                    }
                }
            }

            RunColdTickIfDue();

            _logicThreadActiveTicks += localTimer.ElapsedTicks - start;
            _logicMeter.Tick();
            _backPressure?.Update(logicSw.Elapsed.TotalMilliseconds);

            if (_logicMeter.AverageCyclesPerSecond > 125)
            {
                ThreadTracker.SafeYield();
            }
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

    protected override void Cleanup(NetState state)
    {
        base.Cleanup(state);
        _backPressure?.OnDispose(state);
    }

    protected override void OnSent(NetState state, int sent)
    {
        _backPressure.OnSend(state, sent);
    }

    protected override void OnStopped()
    {
        _netThread?.Join();
        _logicThread?.Join();
        _reportUpdater?.Join();
    }

    protected override void ReportStats()
    {
        ulong uptime = (_stopped > 0 ? _stopped : (ulong)Environment.TickCount64) - _started;
        long total = _netThreadActiveTicks + _logicThreadActiveTicks + 1;
        long netPercent = _netThreadActiveTicks * 100 / total;
        long logicPercent = _logicThreadActiveTicks * 100 / total;

        var stats = new ConnectionStats();
        stats.AddMetric("connections", _states.Count);
        stats.AddMetric("bytesReceived", _bytesReceived);
        stats.AddMetric("bytesWritten", _bytesWritten);
        stats.AddMetric("bytesSent", _bytesSent);
        stats.AddMetric("bytesBacklog", _bytesWritten - _bytesSent);
        stats.AddMetric("packetsProcessed", _packets);
        stats.AddMetric("uptimeMs", uptime);
        stats.AddMetric("threadLogic%", logicPercent);
        stats.AddMetric("threadNet%", netPercent);
        stats.AddMetric("cyclesLogic", _logicMeter.AverageCyclesPerSecond);
        stats.AddMetric("cyclesNet", _netMeter.AverageCyclesPerSecond);
        // GC метрики
        var gcPauseMs = _gcMetrics.GetAndResetTotalPause();
        stats.AddMetric("gcPauseMs", gcPauseMs);

        // Thread метрики
        var threadMetrics = _netMetrics.GetSnapshot();
        stats.AddMetric("netSleepTicks", threadMetrics.TotalSleepTicks);
        stats.AddMetric("netSleepRate", (double)threadMetrics.TotalSleepTicks / (double)total);
        stats.AddMetric("netYieldCount", threadMetrics.TotalYieldCount);
        stats.AddMetric("netYieldRate", (double)threadMetrics.TotalYieldCount / (double)total);
        threadMetrics = _logicMetrics.GetSnapshot();
        stats.AddMetric("logicSleepTicks", threadMetrics.TotalSleepTicks);
        stats.AddMetric("logicSleepRate", (double)threadMetrics.TotalSleepTicks / (double)total);
        stats.AddMetric("logicYieldCount", threadMetrics.TotalYieldCount);
        stats.AddMetric("logicYieldRate", (double)threadMetrics.TotalYieldCount / (double)total);
        _gcUsageMetrics.GenerateMetrics(stats);
        stats.AddMetric("latencyMs", _latencyAggregator.Average());
        _latencyAggregator.Reset();

        // Contention метрики
        //var (contentionCount, contentionTicks) = _contentionService.GetAndReset();
        //stats.AddMetric("contentionCount", contentionCount);
        //stats.AddMetric("contentionTimeTicks", contentionTicks);

        _backPressure.OnUpdateMetrics?.Invoke(stats);
        _collector?.Report(stats);
    }
}