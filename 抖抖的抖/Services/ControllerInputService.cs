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
    private const int ButtonDebounceMilliseconds = 1;

    private readonly XInputControllerService _xInputControllerService;
    private readonly HidControllerService _hidControllerService;
    private readonly HighPrecisionTimer _timer = new();
    private readonly object _stateGate = new();

    private CancellationTokenSource? _cts;
    private Thread? _thread;
    private volatile int _pollingRateHz = 1000;
    private long _exceptionCount;
    private ControllerState _latestState = new();
    private ControllerState? _lastPublishedState;
    private HashSet<string> _previousPressedButtons = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _stablePressedButtons = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _candidatePressedButtons = new(StringComparer.OrdinalIgnoreCase);
    private long _candidateSinceTicks;

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

        _lastPublishedState = null;
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
        var smoothedActualHz = 0.0;
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
                var state = ApplyButtonStateMachine(ApplyButtonDebounce(ReadCurrentState()));
                latencyStopwatch.Stop();

                latencyAverage = latencyAverage <= 0
                    ? latencyStopwatch.Elapsed.TotalMilliseconds
                    : latencyAverage * 0.92 + latencyStopwatch.Elapsed.TotalMilliseconds * 0.08;

                lock (_stateGate)
                {
                    _latestState = state.Clone();
                }

                if (ShouldPublishState(state))
                {
                    _lastPublishedState = state.Clone();
                    PublishState(state);
                }

                loopCounter++;

                if (metricsStopwatch.ElapsedMilliseconds >= 1000)
                {
                    var instantHz = loopCounter * 1000.0 / Math.Max(1, metricsStopwatch.ElapsedMilliseconds);
                    smoothedActualHz = smoothedActualHz <= 0
                        ? instantHz
                        : smoothedActualHz * 0.75 + instantHz * 0.25;
                    PublishMetrics(new InputMetrics(currentRate, smoothedActualHz, latencyAverage, Interlocked.Read(ref _exceptionCount)));
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

    private void PublishState(ControllerState state)
    {
        try
        {
            StateReceived?.Invoke(this, state);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _exceptionCount);
            Log?.Invoke($"状态订阅处理异常，已忽略 UI 侧错误：{ex.Message}");
        }
    }

    private void PublishMetrics(InputMetrics metrics)
    {
        try
        {
            MetricsUpdated?.Invoke(this, metrics);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _exceptionCount);
            Log?.Invoke($"性能指标订阅处理异常，已忽略 UI 侧错误：{ex.Message}");
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

    private ControllerState ApplyButtonDebounce(ControllerState state)
    {
        if (!state.IsConnected)
        {
            return state;
        }

        var nowTicks = Environment.TickCount64;
        if (!state.PressedButtons.SetEquals(_candidatePressedButtons))
        {
            _candidatePressedButtons = new HashSet<string>(state.PressedButtons, StringComparer.OrdinalIgnoreCase);
            _candidateSinceTicks = nowTicks;
        }

        if (_candidatePressedButtons.SetEquals(_stablePressedButtons)
            || nowTicks - _candidateSinceTicks >= ButtonDebounceMilliseconds)
        {
            _stablePressedButtons = new HashSet<string>(_candidatePressedButtons, StringComparer.OrdinalIgnoreCase);
        }

        state.PressedButtons = new HashSet<string>(_stablePressedButtons, StringComparer.OrdinalIgnoreCase);
        return state;
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
            _stablePressedButtons.Clear();
            _candidatePressedButtons.Clear();
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

    private bool ShouldPublishState(ControllerState state)
    {
        if (_lastPublishedState is null)
        {
            return true;
        }

        return state.IsConnected != _lastPublishedState.IsConnected
               || state.ControllerType != _lastPublishedState.ControllerType
               || state.XInputUserIndex != _lastPublishedState.XInputUserIndex
               || !string.Equals(state.DeviceName, _lastPublishedState.DeviceName, StringComparison.OrdinalIgnoreCase)
               || AxisChanged(state.LeftStickX, _lastPublishedState.LeftStickX)
               || AxisChanged(state.LeftStickY, _lastPublishedState.LeftStickY)
               || AxisChanged(state.RightStickX, _lastPublishedState.RightStickX)
               || AxisChanged(state.RightStickY, _lastPublishedState.RightStickY)
               || AxisChanged(state.LeftTrigger, _lastPublishedState.LeftTrigger)
               || AxisChanged(state.RightTrigger, _lastPublishedState.RightTrigger)
               || !state.PressedButtons.SetEquals(_lastPublishedState.PressedButtons)
               || !DictionaryEquals(state.ButtonPhases, _lastPublishedState.ButtonPhases);
    }

    private static bool AxisChanged(double left, double right)
    {
        return Math.Abs(left - right) > 0.02;
    }

    private static bool DictionaryEquals(Dictionary<string, ButtonPhase> left, Dictionary<string, ButtonPhase> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var other) || other != value)
            {
                return false;
            }
        }

        return true;
    }

    public void Dispose()
    {
        Stop();
    }
}
