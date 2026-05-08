using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DouDouDeDou.Controls;
using DouDouDeDou.Models;
using DouDouDeDou.Services;
using DouDouDeDou.Utils;
using Microsoft.Win32;

namespace DouDouDeDou.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ConfigService _configService = new();
    private readonly InputOutputService _inputOutputService = new();
    private readonly SafeReleaseManager _safeReleaseManager = new();
    private readonly XInputControllerService _xInputControllerService = new();
    private readonly HidControllerService _hidControllerService = new();
    private readonly ControllerInputService _controllerInputService;
    private readonly ControllerDetectionService _controllerDetectionService;
    private readonly MappingEngine _mappingEngine;
    private readonly WatchdogService _watchdogService;
    private readonly DispatcherTimer _deviceMonitorTimer;
    private readonly Dictionary<string, DateTimeOffset> _lastLogTimes = new(StringComparer.OrdinalIgnoreCase);

    private PollingRateOption _selectedPollingRate = PollingRateOption.Defaults.First(x => x.Hertz == 1000);
    private ControllerType _previewControllerType = ControllerType.None;
    private string _currentDevice = "未检测到手柄";
    private string _controllerTypeText = "未连接";
    private string _inputModeText = "Auto";
    private string _connectionStatusText = "未连接";
    private string _currentPollingRateText = "1000 Hz";
    private string _vidPidText = "N/A";
    private string _usageText = "N/A";
    private string _actualFrequencyText = "0 Hz";
    private string _averageLatencyText = "0.000 ms";
    private string _exceptionCountText = "0";
    private string _antiStickyStatusText = "待机";
    private string _mappingStatusText = "未开始";
    private bool _isMappingRunning;
    private long _lastUiRefreshTicks;
    private int _uiRefreshPending;

    public MainViewModel()
    {
        _controllerInputService = new ControllerInputService(_xInputControllerService, _hidControllerService);
        _controllerDetectionService = new ControllerDetectionService(_xInputControllerService, _hidControllerService);
        _mappingEngine = new MappingEngine(_inputOutputService, _safeReleaseManager);
        _watchdogService = new WatchdogService(
            _safeReleaseManager,
            _mappingEngine.EnqueueRelease,
            () => _controllerInputService.LatestState,
            _mappingEngine.GetMappingsSnapshot);

        StartMappingCommand = new RelayCommand(_ => StartMapping(), _ => !IsMappingRunning);
        StopMappingCommand = new RelayCommand(_ => StopMapping(), _ => IsMappingRunning);
        SaveConfigCommand = new RelayCommand(_ => SaveConfig());
        LoadConfigCommand = new RelayCommand(_ => LoadConfig());
        AddMappingCommand = new RelayCommand(_ => AddMapping());
        DeleteMappingCommand = new RelayCommand(DeleteMapping, x => x is MappingEntry);
        EditMappingCommand = new RelayCommand(EditMapping, x => x is MappingEntry);

        Mappings.CollectionChanged += OnMappingsChanged;

        _controllerInputService.StateReceived += OnStateReceived;
        _controllerInputService.MetricsUpdated += OnMetricsUpdated;
        _controllerInputService.Faulted += ex => _mappingEngine.ReleaseAll($"输入线程异常：{ex.Message}");
        _controllerInputService.Log += AddLog;
        _mappingEngine.Log += AddLog;
        _watchdogService.Log += AddLog;

        LoadInitialConfig();
        DetectControllerOnce();

        _deviceMonitorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(700)
        };
        _deviceMonitorTimer.Tick += (_, _) =>
        {
            if (!_controllerInputService.IsRunning)
            {
                DetectControllerOnce();
            }
        };
        _deviceMonitorTimer.Start();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<PollingRateOption> PollingRates => PollingRateOption.Defaults;

    public IReadOnlyList<TargetKindOption> TargetKindOptions { get; } =
    [
        new(InputTargetKind.Keyboard, "键盘"),
        new(InputTargetKind.Mouse, "鼠标")
    ];

    public IReadOnlyList<string> KeyboardTargets => KeyCodeHelper.KeyboardTargets;

    public IReadOnlyList<string> MouseTargets => KeyCodeHelper.MouseTargets;

    public IReadOnlyList<string> TargetValues { get; } =
        KeyCodeHelper.KeyboardTargets.Concat(KeyCodeHelper.MouseTargets).ToList();

    public IReadOnlyList<string> SourceButtonOptions { get; } =
    [
        "A", "B", "X", "Y",
        "LB / L1", "RB / R1", "LT / L2", "RT / R2",
        "Back / Share / Create", "Start / Options",
        "LeftStick", "RightStick",
        "DPadUp", "DPadDown", "DPadLeft", "DPadRight",
        "Button13", "Button14", "Button15", "Button16"
    ];

    public ObservableCollection<MappingEntry> Mappings { get; } = new();

    public ObservableCollection<string> PressedButtonNames { get; } = new();

    public ObservableCollection<string> Logs { get; } = new();

    public ICommand StartMappingCommand { get; }

    public ICommand StopMappingCommand { get; }

    public ICommand SaveConfigCommand { get; }

    public ICommand LoadConfigCommand { get; }

    public ICommand AddMappingCommand { get; }

    public ICommand DeleteMappingCommand { get; }

    public ICommand EditMappingCommand { get; }

    public PollingRateOption SelectedPollingRate
    {
        get => _selectedPollingRate;
        set
        {
            if (value is null || _selectedPollingRate.Hertz == value.Hertz)
            {
                return;
            }

            _selectedPollingRate = value;
            CurrentPollingRateText = value.DisplayName;
            _controllerInputService.SetPollingRate(value.Hertz);
            AutoSaveConfig();
            OnPropertyChanged();
        }
    }

    public ControllerType PreviewControllerType
    {
        get => _previewControllerType;
        set => SetField(ref _previewControllerType, value);
    }

    public string CurrentDevice
    {
        get => _currentDevice;
        set => SetField(ref _currentDevice, value);
    }

    public string ControllerTypeText
    {
        get => _controllerTypeText;
        set => SetField(ref _controllerTypeText, value);
    }

    public string InputModeText
    {
        get => _inputModeText;
        set => SetField(ref _inputModeText, value);
    }

    public string ConnectionStatusText
    {
        get => _connectionStatusText;
        set => SetField(ref _connectionStatusText, value);
    }

    public string CurrentPollingRateText
    {
        get => _currentPollingRateText;
        set => SetField(ref _currentPollingRateText, value);
    }

    public string VidPidText
    {
        get => _vidPidText;
        set => SetField(ref _vidPidText, value);
    }

    public string UsageText
    {
        get => _usageText;
        set => SetField(ref _usageText, value);
    }

    public string ActualFrequencyText
    {
        get => _actualFrequencyText;
        set => SetField(ref _actualFrequencyText, value);
    }

    public string AverageLatencyText
    {
        get => _averageLatencyText;
        set => SetField(ref _averageLatencyText, value);
    }

    public string ExceptionCountText
    {
        get => _exceptionCountText;
        set => SetField(ref _exceptionCountText, value);
    }

    public string AntiStickyStatusText
    {
        get => _antiStickyStatusText;
        set => SetField(ref _antiStickyStatusText, value);
    }

    public string MappingStatusText
    {
        get => _mappingStatusText;
        set => SetField(ref _mappingStatusText, value);
    }

    public bool IsMappingRunning
    {
        get => _isMappingRunning;
        set
        {
            if (!SetField(ref _isMappingRunning, value))
            {
                return;
            }

            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void LoadInitialConfig()
    {
        var config = _configService.LoadOrCreate();
        var rate = PollingRates.FirstOrDefault(x => x.Hertz == config.PollingRateHz)
                   ?? PollingRates.First(x => x.Hertz == 1000);
        _selectedPollingRate = rate;
        CurrentPollingRateText = rate.DisplayName;
        _controllerInputService.SetPollingRate(rate.Hertz);
        OnPropertyChanged(nameof(SelectedPollingRate));

        Mappings.Clear();
        foreach (var mapping in config.Mappings)
        {
            Mappings.Add(mapping);
        }

        ApplyMappingsToEngine();
        AddLog($"配置已加载：{_configService.UserConfigPath}");
    }

    private void DetectControllerOnce()
    {
        var info = _controllerDetectionService.DetectBest();
        CurrentDevice = info.DeviceName;
        ControllerTypeText = info.ControllerType.ToDisplayName();
        InputModeText = info.InputMode;
        ConnectionStatusText = info.IsConnected ? "已连接" : "未连接";
        VidPidText = FormatVidPid(info.VendorId, info.ProductId);
        UsageText = FormatUsage(info.UsagePage, info.Usage);
        PreviewControllerType = info.ControllerType;
    }

    private void StartMapping()
    {
        ApplyMappingsToEngine();
        _mappingEngine.Start();
        _controllerInputService.SetPollingRate(SelectedPollingRate.Hertz);
        _controllerInputService.Start();
        _watchdogService.Start();
        IsMappingRunning = true;
        MappingStatusText = "正在映射";
        AntiStickyStatusText = "Watchdog 运行中";
        AddLog("开始映射。");
    }

    private void StopMapping()
    {
        _watchdogService.Stop();
        _controllerInputService.Stop();
        _mappingEngine.Stop();
        IsMappingRunning = false;
        MappingStatusText = "已停止";
        AntiStickyStatusText = "已释放全部输出";
        AddLog("停止映射，已释放所有按下状态。");
    }

    private void SaveConfig()
    {
        _configService.Save(BuildConfig());
        AddLog($"配置已保存：{_configService.UserConfigPath}");
    }

    private void LoadConfig()
    {
        var dialog = new OpenFileDialog
        {
            Title = "加载映射配置",
            Filter = "JSON 配置 (*.json)|*.json|所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var config = _configService.LoadFrom(dialog.FileName);
        SelectedPollingRate = PollingRates.FirstOrDefault(x => x.Hertz == config.PollingRateHz)
                              ?? PollingRates.First(x => x.Hertz == 1000);
        Mappings.Clear();
        foreach (var mapping in config.Mappings)
        {
            Mappings.Add(mapping);
        }

        ApplyMappingsToEngine();
        AutoSaveConfig();
        AddLog($"配置已加载：{dialog.FileName}");
    }

    private void AddMapping()
    {
        Mappings.Add(new MappingEntry
        {
            SourceButton = "A",
            Target = new InputTarget { Kind = InputTargetKind.Keyboard, Value = "Space" },
            IsEnabled = true
        });
        ApplyMappingsToEngine();
        AutoSaveConfig();
        AddLog("已添加一条映射，点击“编辑”选择输出目标。");
    }

    private void DeleteMapping(object? parameter)
    {
        if (parameter is not MappingEntry mapping)
        {
            return;
        }

        Mappings.Remove(mapping);
        _mappingEngine.ReleaseAll("删除映射后刷新状态");
        ApplyMappingsToEngine();
        AutoSaveConfig();
        AddLog($"已删除映射：{mapping.SourceButton} -> {mapping.Target.DisplayName}");
    }

    private void EditMapping(object? parameter)
    {
        if (parameter is not MappingEntry mapping)
        {
            return;
        }

        var dialog = new InputCaptureDialog(mapping.Target.Clone())
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true && dialog.SelectedTarget is not null)
        {
            mapping.Target = dialog.SelectedTarget.Clone();
            ApplyMappingsToEngine();
            AutoSaveConfig();
            AddLog($"映射已更新：{mapping.SourceButton} -> {mapping.Target.DisplayName}");
        }
    }

    private void OnStateReceived(object? sender, ControllerState state)
    {
        _mappingEngine.HandleState(state);
        QueueUiRefresh(state);
    }

    private void OnMetricsUpdated(object? sender, InputMetrics metrics)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            ActualFrequencyText = $"{metrics.ActualFrequencyHz:F0} Hz";
            AverageLatencyText = $"{metrics.AverageLatencyMs:F3} ms";
            ExceptionCountText = metrics.ExceptionCount.ToString();
        });
    }

    private void QueueUiRefresh(ControllerState state)
    {
        var nowTicks = Environment.TickCount64;
        if (nowTicks - _lastUiRefreshTicks < 17)
        {
            return;
        }

        _lastUiRefreshTicks = nowTicks;
        if (Interlocked.Exchange(ref _uiRefreshPending, 1) == 1)
        {
            return;
        }

        var snapshot = state.Clone();
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                UpdateUiState(snapshot);
            }
            finally
            {
                Interlocked.Exchange(ref _uiRefreshPending, 0);
            }
        });
    }

    private void UpdateUiState(ControllerState state)
    {
        CurrentDevice = state.DeviceName;
        ControllerTypeText = state.ControllerType.ToDisplayName();
        InputModeText = state.InputMode;
        ConnectionStatusText = state.IsConnected ? "已连接" : "未连接";
        VidPidText = state.VidPidText;
        UsageText = state.UsageText;
        PreviewControllerType = state.ControllerType;

        PressedButtonNames.Clear();
        foreach (var button in state.GetPressedButtonsSnapshot())
        {
            PressedButtonNames.Add(button);
        }
    }

    private MappingConfig BuildConfig()
    {
        return new MappingConfig
        {
            PollingRateHz = SelectedPollingRate.Hertz,
            InputMode = "Auto",
            Mappings = Mappings.Select(x => x.Clone()).ToList()
        };
    }

    private IReadOnlyList<MappingEntry> GetMappingSnapshot()
    {
        return Mappings.Select(x => x.Clone()).ToList();
    }

    private void ApplyMappingsToEngine()
    {
        _mappingEngine.UpdateMappings(GetMappingSnapshot());
    }

    private void OnMappingsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (MappingEntry mapping in e.OldItems)
            {
                mapping.PropertyChanged -= OnMappingPropertyChanged;
                mapping.Target.PropertyChanged -= OnMappingPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (MappingEntry mapping in e.NewItems)
            {
                mapping.PropertyChanged += OnMappingPropertyChanged;
                mapping.Target.PropertyChanged += OnMappingPropertyChanged;
            }
        }

        if (_mappingEngine.IsRunning)
        {
            _mappingEngine.ReleaseAll("映射列表变更");
        }

        ApplyMappingsToEngine();
        AutoSaveConfig();
    }

    private void OnMappingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_mappingEngine.IsRunning)
        {
            _mappingEngine.ReleaseAll("映射内容修改");
        }

        ApplyMappingsToEngine();
        AutoSaveConfig();
    }

    private void AutoSaveConfig()
    {
        try
        {
            _configService.Save(BuildConfig());
        }
        catch (Exception ex)
        {
            AddLog($"自动保存失败：{ex.Message}");
        }
    }

    private static string FormatVidPid(int? vendorId, int? productId)
    {
        return vendorId is null || productId is null
            ? "N/A"
            : $"VID_{vendorId.Value:X4} / PID_{productId.Value:X4}";
    }

    private static string FormatUsage(int? usagePage, int? usage)
    {
        return usagePage is null || usage is null
            ? "N/A"
            : $"0x{usagePage.Value:X2} / 0x{usage.Value:X2}";
    }

    private void AddLog(string message)
    {
        var now = DateTimeOffset.Now;
        if (_lastLogTimes.TryGetValue(message, out var last) && (now - last).TotalSeconds < 2)
        {
            return;
        }

        _lastLogTimes[message] = now;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            Logs.Insert(0, $"[{DateTime.Now:HH:mm:ss.fff}] {message}");
            while (Logs.Count > 160)
            {
                Logs.RemoveAt(Logs.Count - 1);
            }
        });
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        _deviceMonitorTimer.Stop();
        AutoSaveConfig();
        StopMapping();
        _watchdogService.Dispose();
        _controllerInputService.Dispose();
        _mappingEngine.Dispose();
        _hidControllerService.Dispose();
        _inputOutputService.ReleaseAllPhysical();
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter)
    {
        return _canExecute?.Invoke(parameter) ?? true;
    }

    public void Execute(object? parameter)
    {
        _execute(parameter);
    }
}

public sealed record TargetKindOption(InputTargetKind Kind, string DisplayName);
