using DouDouDeDou.Models;
using DouDouDeDou.Utils;

namespace DouDouDeDou.Services;

public sealed class WatchdogService : IDisposable
{
    private readonly SafeReleaseManager _safeReleaseManager;
    private readonly Action<InputTarget> _releaseOutput;
    private readonly Func<ControllerState> _stateProvider;
    private readonly Func<IReadOnlyList<MappingEntry>> _mappingProvider;

    private CancellationTokenSource? _cts;
    private Thread? _thread;

    public WatchdogService(
        SafeReleaseManager safeReleaseManager,
        Action<InputTarget> releaseOutput,
        Func<ControllerState> stateProvider,
        Func<IReadOnlyList<MappingEntry>> mappingProvider)
    {
        _safeReleaseManager = safeReleaseManager;
        _releaseOutput = releaseOutput;
        _stateProvider = stateProvider;
        _mappingProvider = mappingProvider;
    }

    public event Action<string>? Log;

    public bool IsRunning => _cts is not null;

    public void Start()
    {
        if (_cts is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _thread = new Thread(() => WatchLoop(_cts.Token))
        {
            IsBackground = true,
            Name = "抖抖的抖 Watchdog",
            Priority = ThreadPriority.Normal
        };
        _thread.Start();
        Log?.Invoke("Watchdog 已启动。");
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
        _thread?.Join(300);
        cts.Dispose();
        _thread = null;
        Log?.Invoke("Watchdog 已停止。");
    }

    private void WatchLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                CheckOnce();
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Watchdog 异常：{ex.Message}");
            }

            token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(50));
        }
    }

    private void CheckOnce()
    {
        var state = _stateProvider();
        var mappings = _mappingProvider().ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var activeOutputs = _safeReleaseManager.Snapshot();

        foreach (var active in activeOutputs)
        {
            var shouldStillBeDown = state.IsConnected
                                    && mappings.TryGetValue(active.MappingId, out var mapping)
                                    && mapping.IsEnabled
                                    && state.IsPressed(active.SourceButton);

            if (shouldStillBeDown)
            {
                continue;
            }

            var action = _safeReleaseManager.MarkReleased(active.MappingId, "Watchdog 自动释放");
            if (action is null || !action.ShouldSendPhysicalRelease)
            {
                continue;
            }

            _releaseOutput(action.Target);
            Log?.Invoke($"防粘键：已自动释放 {action.Target.DisplayName}。");
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
