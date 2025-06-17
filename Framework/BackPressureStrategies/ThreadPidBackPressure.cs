using Framework.BackPressureStrategies;
using NETwork;
using System.Collections.Concurrent;
using System.Diagnostics;

public class ThreadPidBackPressure : IBackPressureStrategy
{
    private readonly IThreadStats _stats;
    private readonly PIDController _pid;
    private readonly double _targetLogicMs;
    private double _lastUpdateTime;
    private readonly Stopwatch _sw = Stopwatch.StartNew();

    private int _allowedBytesPerConn = 2048;
    private readonly int _minBytes = 512;
    private readonly int _maxBytes = 8192;
    private readonly ConcurrentDictionary<int, int> _pendingBytesByConn = new();

    public double LastLogicMs { get; private set; }
    public double LastPidOutput { get; private set; }
    public int AllowedBytesPerConn => _allowedBytesPerConn;

    private long _lastLogicTicks;
    private double _smoothedLogicMs = 0;
    private readonly double _smoothingAlpha = 0.2;
    private double _lastRecvRateBps = 0;
    private double _lastPps = 0;

    public Action<ConnectionStats>? OnUpdateMetrics { get; set; }

    public ThreadPidBackPressure(IThreadStats stats, double targetLogicMs = 20.0)
    {
        _stats = stats;
        _targetLogicMs = targetLogicMs;
        _pid = new PIDController(kp: 0.3, ki: 0.03, kd: 0.02, minOutput: 0.2, maxOutput: 1.0);
        _lastLogicTicks = _stats.LogicThreadActiveTicks;
        _lastUpdateTime = _sw.Elapsed.TotalSeconds;

        OnUpdateMetrics = stats =>
        {
            stats.AddMetric("logicMs", LastLogicMs);
            stats.AddMetric("pidOutput", LastPidOutput);
            stats.AddMetric("allowedBytes", AllowedBytesPerConn);
            stats.AddMetric("recvRateBps", _lastRecvRateBps);
            stats.AddMetric("pps", _lastPps);
        };
    }

    public void Update(double logicMs)
    {
        double now = _sw.Elapsed.TotalSeconds;
        double delta = now - _lastUpdateTime;
        if (delta < 0.1) return;

        long currentLogicTicks = _stats.LogicThreadActiveTicks;
        long logicDeltaTicks = currentLogicTicks - _lastLogicTicks;
        _lastLogicTicks = currentLogicTicks;

        _smoothedLogicMs = _smoothedLogicMs * (1 - _smoothingAlpha) + logicMs * _smoothingAlpha;
        LastLogicMs = _smoothedLogicMs;

        LastPidOutput = _pid.Update(_targetLogicMs, LastLogicMs, delta);
        _allowedBytesPerConn = (int)Math.Clamp(LastPidOutput * _maxBytes, _minBytes, _maxBytes);

        _lastUpdateTime = now;
    }

    public bool ShouldPauseRecv(NetState state)
    {
        return _pendingBytesByConn.TryGetValue(state.Id, out var pending) && pending >= _allowedBytesPerConn;
    }

    public bool ShouldPauseLogic(NetState state) => false;

    public void OnReceive(NetState state, int bytes)
    {
        _pendingBytesByConn.AddOrUpdate(state.Id, bytes, (_, old) => old + bytes);
    }

    public void OnProcess(NetState state, int bytes)
    {
        _pendingBytesByConn.AddOrUpdate(state.Id, 0, (_, old) => Math.Max(0, old - bytes));
    }

    public void OnSend(NetState state, int bytes) { }

    private class PIDController
    {
        private readonly double _kp, _ki, _kd;
        private double _integral, _previousError;
        private readonly double _minOutput, _maxOutput;
        private const double DeadzoneMs = 5.0;
        private double _lastOutput = 0;

        public PIDController(double kp, double ki, double kd, double minOutput = 0.2, double maxOutput = 1)
        {
            _kp = kp;
            _ki = ki;
            _kd = kd;
            _minOutput = minOutput;
            _maxOutput = maxOutput;
        }

        public double Update(double setpoint, double actual, double deltaTime)
        {
            double error = setpoint - actual;
            if (Math.Abs(error) < DeadzoneMs)
                return _lastOutput;

            _integral += error * deltaTime;
            _integral = Math.Clamp(_integral, -100, 100);
            double derivative = (error - _previousError) / deltaTime;
            _previousError = error;

            double output = (_kp * error) + (_ki * _integral) + (_kd * derivative);
            _lastOutput = Math.Clamp(output, _minOutput, _maxOutput);
            return _lastOutput;
        }
    }
}
