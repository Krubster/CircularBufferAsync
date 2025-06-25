using Framework.BackPressureStrategies;
using NETwork;
using System.Collections.Concurrent;
using System.Diagnostics;

public class DualPidBackPressure : IBackPressureStrategy
{
    private readonly IThreadStats _stats;
    private readonly PIDController _logicPid;
    private readonly PIDController _sendPid;

    private readonly double _targetLogicMs;
    private readonly double _targetSendMs;
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private double _lastUpdateTime;

    private readonly int _minBytes = 512;
    private readonly int _maxBytes = 8192;

    private int _recvAllowedBytes = 2048;
    private int _sendAllowedBytes = 2048 * 2;

    private readonly ConcurrentDictionary<int, int> _recvPendingBytes = new();
    private readonly ConcurrentDictionary<int, int> _sendPendingBytes = new();

    public double LastLogicMs { get; private set; }
    public double LastSendMs { get; private set; }
    public double LogicPidOutput { get; private set; }
    public double SendPidOutput { get; private set; }

    public Action<ConnectionStats>? OnUpdateMetrics { get; set; }

    private long _lastLogicTicks, _lastSendTicks;
    private double _smoothedLogicMs = 0;
    private readonly double _smoothingAlpha = 0.2;
    private double _lastRecvRateBps = 0;
    private double _lastPps = 0;
    public DualPidBackPressure(IThreadStats stats, double targetLogicMs = 20.0, double targetSendMs = 10.0)
    {
        _stats = stats;
        _targetLogicMs = targetLogicMs;
        _targetSendMs = targetSendMs;

        _logicPid = new PIDController(0.15, 0.05, 0.01, 0.2, 1.0);
        _sendPid = new PIDController(0.15, 0.05, 0.01, 0.2, 1.0);

        _lastLogicTicks = stats.LogicThreadActiveTicks;
        _lastSendTicks = (stats as TriplexConnectionProcessor)?.SendThreadActiveTicks ?? 0;

        _lastUpdateTime = _sw.Elapsed.TotalSeconds;

        OnUpdateMetrics = stats =>
        {
            stats.AddMetric("logicMs", LastLogicMs);
            stats.AddMetric("sendMs", LastSendMs);
            stats.AddMetric("recvAllowed", _recvAllowedBytes);
            stats.AddMetric("sendAllowed", _sendAllowedBytes);
            stats.AddMetric("logicPid", LogicPidOutput);
            stats.AddMetric("sendPid", SendPidOutput);
        };
    }

    public bool ShouldPauseNet()
    {
        // soft thresholds
        bool logicOverloaded = LastLogicMs > 25;
        bool sendOverloaded = LastSendMs > 15;

        return logicOverloaded || sendOverloaded;
    }

    public void Update(double logicMs)
    {
        double now = _sw.Elapsed.TotalSeconds;
        double delta = now - _lastUpdateTime;
        if (delta < 0.1) return;

        long currentLogicTicks = _stats.LogicThreadActiveTicks;
        // long logicDeltaTicks = currentLogicTicks - _lastLogicTicks;
        _lastLogicTicks = currentLogicTicks;

        _smoothedLogicMs = _smoothedLogicMs * (1 - _smoothingAlpha) + logicMs * _smoothingAlpha;
        LastLogicMs = _smoothedLogicMs;
        LogicPidOutput = _logicPid.Update(_targetLogicMs, LastLogicMs, delta);
        _recvAllowedBytes = (int)Math.Clamp(LogicPidOutput * _maxBytes, _minBytes, _maxBytes);

        // Send
        if (_stats is TriplexConnectionProcessor t)
        {
            long sendTicks = t.SendThreadActiveTicks;
            long sendDelta = sendTicks - _lastSendTicks;
            _lastSendTicks = sendTicks;

            double sendMs = (sendDelta / (double)Stopwatch.Frequency) * 1000.0 / delta;
            LastSendMs = Math.Clamp(sendMs, 0, 100);
            SendPidOutput = _sendPid.Update(_targetSendMs, LastSendMs, delta);
            _sendAllowedBytes = (int)Math.Clamp(SendPidOutput * _maxBytes, _minBytes, _maxBytes);
        }

        _lastUpdateTime = now;
    }

    public bool ShouldPauseRecv(NetState state)
    {
        int recv = _recvPendingBytes.TryGetValue(state.Id, out var r) ? r : 0;
        int sendBuffered = (int)state.SendBuffer.WrittenBytes;

        return recv >= _recvAllowedBytes || sendBuffered >= 1024;
    }

    public bool ShouldPauseLogic(NetState state) => false;

    public bool ShouldPauseSend(NetState state)
    {
        return false;
    }

    public void OnReceive(NetState state, int bytes)
    {
        _recvPendingBytes.AddOrUpdate(state.Id, bytes, (_, old) => old + bytes);
    }

    public void OnProcess(NetState state, int bytes)
    {
        _recvPendingBytes.AddOrUpdate(state.Id,  -bytes, (_, old) => Math.Max(0, old - bytes));
    }

    public void OnSend(NetState state, int bytes)
    {
        _sendPendingBytes.AddOrUpdate(state.Id, bytes, (_, old) => old + bytes);
    }

    public void OnDispose(NetState state)
    {
        if (_sendPendingBytes.ContainsKey(state.Id))
            _sendPendingBytes.Remove(state.Id, out var _);

        if (_recvPendingBytes.ContainsKey(state.Id))
            _recvPendingBytes.Remove(state.Id, out var _);
    }
}