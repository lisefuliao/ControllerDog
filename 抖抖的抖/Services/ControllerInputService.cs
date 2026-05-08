using System.Diagnostics;
using DouDouDeDou.Models;
using DouDouDeDou.Utils;

namespace DouDouDeDou.Services;

public sealed record InputMetrics(
    int PollingRateHz,
    double ActualFrequencyHz,
    double AverageLatencyMs,
    long ExceptionCount);

public sealed class ControllerInputService : IDisposable
{
    private readonly XInputControllerService _xInputControllerService;
    private readonly HidControllerService _hidControllerService;
    private readonly HighPrecisionTimer _timer = new();
    private readonly object _stateGate = new();

    private CancellationTokenSource? _cts;
    private Thread? _thread;
    private volatile int _pollingRateHz = 1000;
    private long _exceptionCount;
    private ControllerState _latestState = new();
    private HashSet<string> _previousPressedButtons = new(StringComparer.OrdinalIgnoreCase);

    public ControllerInputService(
        XInputControllerService xInputControllerService,
        HidControllerService hidControllerService)
    {
        _xInputControllerService = xInputControllerService;
        _hidControllerService = hidControllerService;
    }

    public event EventHandler<ControllerState>? StateReceived;

    public event EventHandler<InputMetrics>? MetricsUpdated;

    public event Action<string>? Log;

    public event Action<Exception>? Faulted;

    public bool IsRunning => _cts is not null;

    public ControllerState LatestState
    {
        get
        {
            lock (_stateGate)
            {
                return _latestState.Clone();
            }
        }
    }

    public void SetPollingRate(int hertz)
    {
        _pollingRateHz = Math.Clamp(hertz, 250, 8000);
    }

    public void Start()
    {
        if (_cts is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _thread = new Thread(() => PollLoop(_cts.Token))
        {
            IsBackground = true,
            Name = "抖抖的抖 输入读取线程",
            Priority = ThreadPriority.AboveNormal
        };
        _thread.Start();
        Log?.Invoke("输入读取线程已启动。");
    }

    public void Stop()
    {
        var cts = _cts;
        if (cts is null)
        {
            return;
        }

        _cts = null;
        cts.Cancel();
        if (_thread is not null && !_thread.Join(300))
        {
            Log?.Invoke("输入读取线程正在结束，已请求安全停止。");
        }

        cts.Dispose();
        _thread = null;
        Log?.Invoke("输入读取线程已停止。");
    }

    private void PollLoop(CancellationToken cancellationToken)
    {
        var currentRate = _pollingRateHz;
        _timer.Reset(currentRate);

        var metricsStopwatch = Stopwatch.StartNew();
        var loopCounter = 0;
        var latencyAverage = 0.0;
        var latencyStopwatch = new Stopwatch();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (currentRate != _pollingRateHz)
                {
                    currentRate = _pollingRateHz;
                    _timer.Reset(currentRate);
                    Log?.Invoke($"轮询率已切换为 {currentRate} Hz。");
                }

                latencyStopwatch.Restart();
                var state = ApplyButtonStateMachine(ReadCurrentState());
                latencyStopwatch.Stop();

                latencyAverage = latencyAverage <= 0
                    ? latencyStopwatch.Elapsed.TotalMilliseconds
                    : latencyAverage * 0.92 + latencyStopwatch.Elapsed.TotalMilliseconds * 0.08;

                lock (_stateGate)
                {
                    _latestState = state.Clone();
                }

                StateReceived?.Invoke(this, state);
                loopCounter++;

                if (metricsStopwatch.ElapsedMilliseconds >= 250)
                {
                    var actualHz = loopCounter * 1000.0 / Math.Max(1, metricsStopwatch.ElapsedMilliseconds);
                    MetricsUpdated?.Invoke(this, new InputMetrics(currentRate, actualHz, latencyAverage, Interlocked.Read(ref _exceptionCount)));
                    loopCounter = 0;
                    metricsStopwatch.Restart();
                }

                _timer.WaitForNextTick(cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Interlocked.Increment(ref _exceptionCount);
                Faulted?.Invoke(ex);
                Log?.Invoke($"输入线程异常，已触发安全释放：{ex.Message}");
                Thread.Sleep(20);
            }
        }
    }

    private ControllerState ReadCurrentState()
    {
        if (_xInputControllerService.TryGetFirstConnectedState(out var xInputState))
        {
            return xInputState;
        }

        if (_hidControllerService.TryReadState(out var hidState))
        {
            return hidState;
        }

        return new ControllerState
        {
            IsConnected = false,
            DeviceName = "未检测到手柄",
            ControllerType = ControllerType.None,
            InputMode = "Auto",
            Timestamp = DateTimeOffset.Now
        };
    }

    private ControllerState ApplyButtonStateMachine(ControllerState state)
    {
        var phases = new Dictionary<string, ButtonPhase>(StringComparer.OrdinalIgnoreCase);

        if (!state.IsConnected)
        {
            foreach (var previous in _previousPressedButtons)
            {
                phases[previous] = ButtonPhase.Released;
            }

            _previousPressedButtons.Clear();
            state.ButtonPhases = phases;
            return state;
        }

        foreach (var button in state.PressedButtons)
        {
            phases[button] = _previousPressedButtons.Contains(button)
                ? ButtonPhase.Held
                : ButtonPhase.Down;
        }

        foreach (var previous in _previousPressedButtons)
        {
            if (!state.PressedButtons.Contains(previous))
            {
                phases[previous] = ButtonPhase.Released;
            }
        }

        _previousPressedButtons = new HashSet<string>(state.PressedButtons, StringComparer.OrdinalIgnoreCase);
        state.ButtonPhases = phases;
        return state;
    }

    public void Dispose()
    {
        Stop();
    }
}
