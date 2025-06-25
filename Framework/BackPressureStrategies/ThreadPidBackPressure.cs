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
    private readonly int _minBytes = 2048;
    private readonly int _maxBytes = 8192*2;
    private readonly ConcurrentDictionary<int, int> _pendingBytesByConn = new();

    public double LastLogicMs { get; private set; }
    public double LastPidOutput { get; private set; }
    public int AllowedBytesPerConn => _allowedBytesPerConn;

    private double _smoothedLogicMs = 0;
    private readonly double _smoothingAlpha = 0.2;
    private double _lastRecvRateBps = 0;
    private double _lastPps = 0;

    public Action<ConnectionStats>? OnUpdateMetrics { get; set; }

    public ThreadPidBackPressure(IThreadStats stats, double targetLogicMs = 20.0)
    {
        _stats = stats;
        _targetLogicMs = targetLogicMs;
        _pid = new PIDController(kp: 0.15, ki: 0.05, kd: 0.01, minOutput: 0.2, maxOutput: 1.0, true);
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

    public void OnDispose(NetState state)
    {
        if(_pendingBytesByConn.ContainsKey(state.Id))
            _pendingBytesByConn.Remove(state.Id, out var _);
    }

    public void OnSend(NetState state, int bytes) { }

    public bool ShouldPauseNet()
    {
        throw new NotImplementedException();
    }
}
public class PIDController
{
    private double _kp, _ki, _kd;
    private double _integral, _previousError;
    private readonly double _minOutput, _maxOutput;
    private double _lastOutput = 0;

    private readonly bool _enableAutoTune;

    // Для автонастройки
    private readonly Queue<double> _errorHistory = new();
    private readonly int _historySize = 20;

    public PIDController(
        double kp, double ki, double kd,
        double minOutput = 0.2, double maxOutput = 1,
        bool enableAutoTune = false)
    {
        _kp = kp;
        _ki = ki;
        _kd = kd;
        _minOutput = minOutput;
        _maxOutput = maxOutput;
        _enableAutoTune = enableAutoTune;
    }

    public double Update(double setpoint, double actual, double deltaTime)
    {
        double error = setpoint - actual;

        _integral += error * deltaTime;
        _integral = Math.Clamp(_integral, -100, 100);

        double derivative = (error - _previousError) / deltaTime;
        _previousError = error;

        double output = (_kp * error) + (_ki * _integral) + (_kd * derivative);
        _lastOutput = Math.Clamp(output, _minOutput, _maxOutput);

        if (_enableAutoTune)
            RecordAndTune(error);

        return _lastOutput;
    }

    private void RecordAndTune(double error)
    {
        _errorHistory.Enqueue(error);
        if (_errorHistory.Count > _historySize)
            _errorHistory.Dequeue();

        if (_errorHistory.Count < _historySize)
            return;

        var errors = _errorHistory.ToArray();
        double avg = errors.Average();
        double variance = errors.Select(e => (e - avg) * (e - avg)).Average();

        if (variance < 1.0 && Math.Abs(avg) < 2.0)
        {
            _kp *= 1.01;
            _kp = Math.Min(_kp, 1.0);
        }
        else if (variance > 8.0)
        {
            _kp *= 0.95;
            _ki *= 1.05;

            _kp = Math.Max(_kp, 0.05);
            _ki = Math.Min(_ki, 0.5);
        }
    }
}