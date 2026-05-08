using System.Collections.Concurrent;
using DouDouDeDou.Models;
using DouDouDeDou.Utils;

namespace DouDouDeDou.Services;

public sealed class MappingEngine : IDisposable
{
    private readonly InputOutputService _inputOutputService;
    private readonly SafeReleaseManager _safeReleaseManager;
    private readonly BlockingCollection<OutputCommand> _outputQueue = new();
    private readonly Thread _outputThread;
    private readonly object _mappingGate = new();

    private List<MappingEntry> _mappings = new();
    private volatile bool _isRunning;

    public MappingEngine(InputOutputService inputOutputService, SafeReleaseManager safeReleaseManager)
    {
        _inputOutputService = inputOutputService;
        _safeReleaseManager = safeReleaseManager;
        _outputThread = new Thread(OutputLoop)
        {
            IsBackground = true,
            Name = "抖抖的抖 输出线程",
            Priority = ThreadPriority.AboveNormal
        };
        _outputThread.Start();
    }

    public event Action<string>? Log;

    public bool IsRunning => _isRunning;

    public void UpdateMappings(IEnumerable<MappingEntry> mappings)
    {
        lock (_mappingGate)
        {
            _mappings = mappings.Select(x => x.Clone()).ToList();
        }
    }

    public IReadOnlyList<MappingEntry> GetMappingsSnapshot()
    {
        lock (_mappingGate)
        {
            return _mappings.Select(x => x.Clone()).ToList();
        }
    }

    public void Start()
    {
        _isRunning = true;
        Log?.Invoke("映射引擎已启动。");
    }

    public void Stop()
    {
        _isRunning = false;
        ReleaseAll("停止映射");
        Log?.Invoke("映射引擎已停止，所有输出已释放。");
    }

    public void HandleState(ControllerState state)
    {
        if (!_isRunning)
        {
            return;
        }

        if (!state.IsConnected)
        {
            ReleaseAll("设备断开");
            return;
        }

        List<MappingEntry> mappings;
        lock (_mappingGate)
        {
            mappings = _mappings.Select(x => x.Clone()).ToList();
        }

        foreach (var mapping in mappings)
        {
            var desiredDown = mapping.IsEnabled && state.IsPressed(mapping.SourceButton);
            if (desiredDown)
            {
                if (_safeReleaseManager.TryMarkPressed(mapping.Id, mapping.SourceButton, mapping.Target, out var shouldSendPhysicalPress)
                    && shouldSendPhysicalPress)
                {
                    _outputQueue.Add(OutputCommand.Press(mapping.Target.Clone()));
                }
            }
            else
            {
                var action = _safeReleaseManager.MarkReleased(mapping.Id, "手柄按键释放或映射禁用");
                if (action is not null && action.ShouldSendPhysicalRelease)
                {
                    _outputQueue.Add(OutputCommand.Release(action.Target.Clone()));
                }
            }
        }
    }

    public void ReleaseAll(string reason)
    {
        var actions = _safeReleaseManager.ReleaseAll(reason);
        foreach (var action in actions)
        {
            _outputQueue.Add(OutputCommand.Release(action.Target.Clone()));
        }
    }

    public void EnqueueRelease(InputTarget target)
    {
        _outputQueue.Add(OutputCommand.Release(target.Clone()));
    }

    private void OutputLoop()
    {
        foreach (var command in _outputQueue.GetConsumingEnumerable())
        {
            try
            {
                if (command.IsPress)
                {
                    _inputOutputService.Press(command.Target);
                }
                else
                {
                    _inputOutputService.Release(command.Target);
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"输出事件失败：{ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        _isRunning = false;
        ReleaseAll("程序退出");
        _outputQueue.CompleteAdding();
        _outputThread.Join(500);
        _inputOutputService.ReleaseAllPhysical();
    }

    private sealed record OutputCommand(bool IsPress, InputTarget Target)
    {
        public static OutputCommand Press(InputTarget target) => new(true, target);

        public static OutputCommand Release(InputTarget target) => new(false, target);
    }
}
