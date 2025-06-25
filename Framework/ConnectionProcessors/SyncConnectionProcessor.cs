using Framework.ConnectionProcessors;
using NETwork;
using System.Net.Sockets;
using Framework.Buffers;
using Framework.Network;
using Framework.Workloads;
using Framework.Metrics;
using System.Buffers.Binary;
using System.Diagnostics;
using System;

public class SyncConnectionProcessor : BaseConnectionProcessor
{
    private Thread? _worker;
    private readonly CpuCycleMeter _mainMeter = new();
    private Thread? _reportUpdater;
    private GCMetricsService _gcMetrics = new();
    private ulong _bytesWritten = 0;

    private long _lastBgWorkloadTicks, _lastReadNetTicks, _lastWriteNetTicks;
    public SyncConnectionProcessor(IPacketProcessor processor, ILogicWorkload logicWorkload, IServerTrafficPattern pattern, IStatsCollector? collector = null)
        : base(processor, collector, logicWorkload, pattern, 2048) { }

    public override void Start(CancellationToken ct)
    {
        _ct = ct;
        _started = (ulong)Environment.TickCount64;
        _worker = new Thread(ProcessLoop) { IsBackground = true };
        _worker.Start();
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
        _reportUpdater.Start();
    }

    private void ProcessLoop()
    {
        while (!_ct.IsCancellationRequested)
        {
            long start = Stopwatch.GetTimestamp();
            // Emulating background work
            _logicWorkload.Execute();
            _lastBgWorkloadTicks = Stopwatch.GetTimestamp() - start;
            
            while (_newConnections.Reader.TryRead(out var socket))
            {
                var state = new NetState(socket, this);
                state.SendPipe = new Pipe(256 * 1024);
                state.UsePipe = true;
                state.BufferPipe = new Pipe(64 * 1024);
                _states.TryAdd(socket, state);
                _pollGroup.Add(socket, state.Handle);
            }
            start = Stopwatch.GetTimestamp();
            int count = _pollGroup.Poll(_polledStates);

            for (int i = 0; i < count; i++)
            {
                if (_polledStates[i].Target is not NetState state) continue;
                try
                {
                    var packets = 0;
                    _bytesReceived += (ulong)state.HandleReceive((payload, conn) =>
                    {
                        LatencyMetrics.MarkPacket(payload);
                        var (processed, timestamps) = Processor.ProcessBuffer(payload, state);
                        _packets += (ulong)processed;
                        packets = processed;
                        // Emulating steady output
                        foreach (uint timestamp in timestamps)
                        {
                            int? packetLen2 = _echoTrafficPattern.GetNextPayloadLength(state);
                            if (packetLen2 != null)
                            {
                                SpanWriter writer = new SpanWriter((int)packetLen2 + 2);
                                writer.Write((ushort)packetLen2);
                                for (int k = 0; k < packetLen2; ++k)
                                    writer.Write((byte)0);

                                LatencyMetrics.MarkPacket(writer.RawBuffer, timestamp);

                                state.Send(writer.Span, (int)(packetLen2 + 2));
                                _bytesWritten += (ulong)(packetLen2 + 2);
                            }
                        }


                    });
                }
                catch { Cleanup(state); }


                // Emulating output
                int? packetLen = _serverTrafficPattern.GetNextPayloadLength(state);
                if (packetLen != null)
                {
                    SpanWriter writer = new SpanWriter((int)packetLen + 2);
                    writer.Write((ushort)packetLen);
                    for (int k = 0; k < packetLen; ++k)
                        writer.Write((byte)0);

                    state.Send(writer.Span, (int)(packetLen + 2));
                    _bytesWritten += (ulong)(packetLen + 2);
                }
                _polledStates[i] = default;
            }

            _lastReadNetTicks = Stopwatch.GetTimestamp() - start;
            start = Stopwatch.GetTimestamp();
            FlushSends();
            _lastWriteNetTicks = Stopwatch.GetTimestamp() - start;
            _mainMeter.Tick();
        }
    }



    protected override void FlushSends()
    {
        while (_flushPending.Reader.TryRead(out var state) && !_ct.IsCancellationRequested)
        {
           // if (state.Socket.Poll(1_000, SelectMode.SelectWrite))
          //  {
                var reader = state.SendPipe.Reader;
                var span = reader.AvailableToRead();
                if (span.Length == 0)
                {
                    continue;
                }

                try
                {
                    // Latency metrics
                    int offset = 0;
                    while (offset + 6 <= span.Length)
                    {
                        ushort len = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(offset));
                        if (offset + len > span.Length)
                            break;

                        uint ts = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset + 2, 4));
                        if (ts != 0)
                        {
                            uint now = (uint)(Stopwatch.GetTimestamp() * 1_000 / Stopwatch.Frequency);
                            uint latency = now - ts;

                            _latencyAggregator.Add(latency); // усреднённый latency
                        }

                        offset += len + 2;
                    }
                    //

                    int sent = state.Socket.Send(span, SocketFlags.None);
                    reader.Advance((uint)sent);
                    state.FlushReset();
                    _bytesSent += (ulong)sent;
                }
                catch
                {
                    Cleanup(state);
                }
          //  }
          //  else
          //  {
           //     _flushThrottled.Writer.TryWrite(state);
           //     state.FlushSet();
           //     Console.WriteLine("Send would block, skipping...");
           // }
        }
    }

    protected override void OnStopped()
    {
        _worker?.Join();
        _reportUpdater?.Join();
    }

    protected override void ReportStats()
    {
        ulong uptime = (_stopped > 0 ? _stopped : (ulong)Environment.TickCount64) - _started;
        var stats = new ConnectionStats();
        stats.AddMetric("connections", _states.Count);
        stats.AddMetric("bytesReceived", _bytesReceived);
        stats.AddMetric("bytesWritten", _bytesWritten);
        stats.AddMetric("bytesSent", _bytesSent);
        stats.AddMetric("bytesBacklog", _bytesWritten - _bytesSent);
        stats.AddMetric("packetsProcessed", _packets);
        stats.AddMetric("uptimeMs", uptime);
        stats.AddMetric("cyclesLogic", _mainMeter.AverageCyclesPerSecond);
        var gcPauseMs = _gcMetrics.GetAndResetTotalPause();
        stats.AddMetric("gcPauseMs", gcPauseMs);
        _gcUsageMetrics.GenerateMetrics(stats);
        stats.AddMetric("latencyMs", _latencyAggregator.Average());
        _latencyAggregator.Reset();

        stats.AddMetric("backgroundMs", _lastBgWorkloadTicks * 1000.0 / Stopwatch.Frequency);
        stats.AddMetric("netAndlogicMs", _lastReadNetTicks * 1000.0 / Stopwatch.Frequency);
        stats.AddMetric("writeMs", _lastWriteNetTicks * 1000.0 / Stopwatch.Frequency);

        _collector?.Report(stats);
    }
}
