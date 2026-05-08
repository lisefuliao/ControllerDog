using System.Diagnostics;
using System.Threading;

namespace DouDouDeDou.Utils;

public sealed class HighPrecisionTimer
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private double _intervalTicks;
    private double _nextTick;

    public int Hertz { get; private set; }

    public void Reset(int hertz)
    {
        Hertz = Math.Max(1, hertz);
        _intervalTicks = Stopwatch.Frequency / (double)Hertz;
        _nextTick = _stopwatch.ElapsedTicks;
    }

    public long WaitForNextTick(CancellationToken cancellationToken)
    {
        _nextTick += _intervalTicks;

        while (!cancellationToken.IsCancellationRequested)
        {
            var remainingTicks = _nextTick - _stopwatch.ElapsedTicks;
            if (remainingTicks <= 0)
            {
                return (long)-remainingTicks;
            }

            var remainingMs = remainingTicks * 1000.0 / Stopwatch.Frequency;

            // 高频轮询不能只靠 Sleep(1)，这里用“粗睡眠 + 让出时间片 + 少量自旋”的混合方式。
            if (remainingMs > 2.0)
            {
                Thread.Sleep(1);
            }
            else if (remainingMs > 0.35)
            {
                Thread.Yield();
            }
            else
            {
                Thread.SpinWait(32);
            }
        }

        return 0;
    }
}
