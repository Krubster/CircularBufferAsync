// Вспомогательный утилитный класс (можно вынести отдельно)

using System.Diagnostics;

public class CpuCycleMeter
{
    private readonly int _interval;
    private readonly int _cycleCount;
    private readonly double[] _cyclesPerSecond;
    private int _cycleIndex = 0, _sample = 0;
    private long _last;
    private readonly double _frequency;
    public double AverageCyclesPerSecond => _cyclesPerSecond.Average();

    public CpuCycleMeter(int interval = 100, int cycleCount = 64)
    {
        _interval = interval;
        _cycleCount = cycleCount;
        _cyclesPerSecond = new double[cycleCount];
        _frequency = Stopwatch.Frequency;
        _last = Stopwatch.GetTimestamp();
    }

    public void Tick()
    {
        if (++_sample < _interval) return;
        _sample = 0;
        long now = Stopwatch.GetTimestamp();

        double cps = _frequency / (now - _last + 1); // +1 для избежания деления на 0
        _cyclesPerSecond[_cycleIndex++] = cps;
        if (_cycleIndex == _cycleCount)
            _cycleIndex = 0;

        _last = now;
    }
}