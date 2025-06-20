using Framework.ConnectionProcessors;
using NETwork;
using System.Net.Sockets;
using Framework.Buffers;
using Framework.Network;
using Framework.Workloads;
using System.Diagnostics;

public class SyncConnectionProcessor : BaseConnectionProcessor
{
    private Thread? _worker;
    private readonly CpuCycleMeter _mainMeter = new();
    private Thread? _reportUpdater;

    public SyncConnectionProcessor(IPacketProcessor processor, ILogicWorkload logicWorkload, IServerTrafficPattern pattern, IStatsCollector? collector = null)
        : base(processor, collector, logicWorkload, pattern, 2048) { }

    public override void Start(CancellationToken ct)
    {
        _ct = ct;
        _started = Environment.TickCount64;
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
            // Emulating background work
            _logicWorkload.Execute();

            while (_newConnections.TryDequeue(out var socket))
            {
                var state = new NetState(socket, this);
                state.SendPipe = new Pipe(256 * 1024);
                state.UsePipe = true;
                state.BufferPipe = new Pipe(64 * 1024);
                _states.TryAdd(socket, state);
                _pollGroup.Add(socket, state.Handle);
            }

            int count = _pollGroup.Poll(_polledStates);
            for (int i = 0; i < count; i++)
            {
                if (_polledStates[i].Target is not NetState state) continue;

                // Emulating output
                int? packetLen = _serverTrafficPattern.GetNextPayloadLength(state);
                if (packetLen != null)
                {
                    SpanWriter writer = new SpanWriter((int)packetLen + 2);
                    writer.Write((ushort)packetLen);
                    for(int k = 0; k < packetLen; ++k)
                        writer.Write((byte)0);

                    state.Send(writer.Span, (int)(packetLen + 2));
                }

                try
                {
                    _bytesReceived += state.HandleReceive((payload, conn) =>
                    {
                        var processed = Processor.ProcessBuffer(payload, state);
                        _packets += processed;
                    });
                }
                catch { Cleanup(state); }
            }

            FlushSends();
            _mainMeter.Tick();
        }
    }

    protected override void FlushSends()
    {
        while (_flushPending.TryDequeue(out var state) && !_ct.IsCancellationRequested)
        {
            var reader = state.SendPipe.Reader;
            var span = reader.AvailableToRead();
            if (span.Length == 0)
            {
                continue;
            }
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

    protected override void OnStopped()
    {
        _worker?.Join();
        _reportUpdater?.Join();
    }

    protected override void ReportStats()
    {
        long uptime = (_stopped > 0 ? _stopped : Environment.TickCount64) - _started;
        var stats = new ConnectionStats();
        stats.AddMetric("connections", _states.Count);
        stats.AddMetric("bytesReceived", _bytesReceived);
        stats.AddMetric("bytesSent", _bytesSent);
        stats.AddMetric("packetsProcessed", _packets);
        stats.AddMetric("uptimeMs", uptime);
        stats.AddMetric("cyclesLogic", _mainMeter.AverageCyclesPerSecond);
        _collector?.Report(stats);
    }
}
