using NETwork.ConnectionProcessors;
using NETwork;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Network;
using System.Runtime.InteropServices;
using Framework.Workloads;
using Framework.Metrics;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Threading.Channels;

namespace Framework.ConnectionProcessors
{
    public abstract class BaseConnectionProcessor : IConnectionProcessor
    {
        public Channel<NetState> FlushPending => _flushPending;
        protected readonly Channel<NetState> _flushPending = Channel.CreateUnbounded<NetState>();
        protected readonly Channel<Socket> _newConnections = Channel.CreateUnbounded<Socket>();

        protected readonly IPacketProcessor Processor;
        protected readonly IStatsCollector? _collector;
        protected readonly ConcurrentDictionary<Socket, NetState> _states = new();
        protected readonly IPollGroup _pollGroup = PollGroup.Create();
        protected readonly GCHandle[] _polledStates;

        protected CancellationToken _ct;
        protected ulong _started, _stopped;

        protected ulong _bytesReceived, _bytesSent, _packets;
        protected ILogicWorkload _logicWorkload;
        protected IServerTrafficPattern _serverTrafficPattern;
        protected IServerTrafficPattern _echoTrafficPattern;
        protected GCUsageMetrics _gcUsageMetrics = new();
        protected LatencyAggregator _latencyAggregator = new();

        protected BaseConnectionProcessor(IPacketProcessor processor, IStatsCollector? collector, ILogicWorkload lw, IServerTrafficPattern pattern, int pollSize)
        {
            Processor = processor;
            _collector = collector;
            _logicWorkload = lw;
            _serverTrafficPattern = pattern;
            _echoTrafficPattern = new EchoServerPacketPattern();
            _polledStates = new GCHandle[pollSize];
        }

        public virtual void Add(Socket socket)
        {
            _newConnections.Writer.TryWrite(socket);
            socket.NoDelay = true;
            socket.SendBufferSize = 65536;
        }

        public abstract void Start(CancellationToken token);

        public virtual void Stop()
        {
            _stopped = (ulong)Environment.TickCount64;
            _ct = new CancellationToken(true);
            OnStopped();
            _collector?.FlushToFile();
        }

        protected abstract void OnStopped();

        public void SetRuntime(string key, string value)
        {
            switch (key)
            {
                case "cpuload":
                    {
                        if (Processor is CpuIntensiveProcessor p)
                        {
                            p.CpuLoad = int.TryParse(value, out var load) ? load : 10;
                        }

                        break;
                    }
                case "sendChance":
                    {
                        if (_serverTrafficPattern is SimpleServerPacketPattern p)
                        {
                            p.SendChance = double.TryParse(value, out var load) ? load : 0.3;
                        }

                        break;
                    }
                case "lowBorder":
                    {
                        if (_serverTrafficPattern is SimpleServerPacketPattern p)
                        {
                            p.LowEnd = int.TryParse(value, out var load) ? load : 30;
                        }

                        break;
                    }
                case "highBorder":
                    {
                        if (_serverTrafficPattern is SimpleServerPacketPattern p)
                        {
                            p.HighEnd = int.TryParse(value, out var load) ? load : 150;
                        }

                        break;
                    }
                case "backgroundWorkload":
                    {
                        if (_logicWorkload is SpinWaitLogicWorkload p)
                        {
                            p.Workload = int.TryParse(value, out var load) ? load : 10000;
                        }

                        break;
                    }
                case "workload":
                    break;
            }
        }

        protected virtual void OnSent(NetState state, int sent)
        {
        }

        protected virtual void FlushSends()
        {
            while (_flushPending.Reader.TryRead(out var state) && !_ct.IsCancellationRequested)
            {
                try
                {
                    while (state.SendBuffer.TryPeek(out var buffer) && !_ct.IsCancellationRequested)
                    {
                        // Latency metrics
                        int offset = 0;
                        while (offset + 6 <= buffer.Length)
                        {
                            ushort len = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(offset));
                            if (offset + len > buffer.Length)
                                break;

                            uint ts = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(offset + 2, 4));
                            if (ts != 0)
                            {
                                uint now = (uint)(Stopwatch.GetTimestamp() * 1_000 / Stopwatch.Frequency);
                                uint latency = now - ts;

                                _latencyAggregator.Add(latency); // усреднённый latency
                            }

                            offset += len + 2;
                        }
                        //

                        int sent = state.Socket.Send(buffer, SocketFlags.None);
                        state.FlushReset();
                        _bytesSent += (ulong)sent;
                        state.SendBuffer.Advance(buffer.Length);
                        OnSent(state, sent);
                    }
                }
                catch (SocketException ex)
                {
                    Console.WriteLine($"[Client #{state.Id}] SocketException ({ex.ErrorCode}): {ex.SocketErrorCode}");
                    Cleanup(state);
                }
            }
        }

        protected virtual void Cleanup(NetState state)
        {
            if (_states.ContainsKey(state.Socket))
            {
                _states.Remove(state.Socket, out _);
                try
                {
                    _pollGroup.Remove(state.Socket, state.Handle);
                    Console.WriteLine($"Closed connection: {state.Id}");

                }
                catch (ObjectDisposedException ex1)
                {
                    Console.WriteLine($"[BaseConnectionProcessor] Dispose error: {ex1}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BaseConnectionProcessor] Cleanup error: {ex}");
                }

                state.Dispose();
            }
        }

        protected abstract void ReportStats();
    }
}
