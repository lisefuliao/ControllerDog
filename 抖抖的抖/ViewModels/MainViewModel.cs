using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
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
    private readonly ThemeService _themeService = new();
    private readonly InputOutputService _inputOutputService = new();
    private readonly SafeReleaseManager _safeReleaseManager = new();
    private readonly XInputControllerService _xInputControllerService = new();
    private readonly HidControllerService _hidControllerService = new();
    private readonly ControllerInputService _controllerInputService;
    private readonly ControllerDetectionService _controllerDetectionService;
    private readonly MappingEngine _mappingEngine;
    private readonly WatchdogService _watchdogService;
    private readonly DispatcherTimer _deviceMonitorTimer;
    private readonly object _pendingUiGate = new();
    private readonly Dictionary<string, DateTimeOffset> _lastLogTimes = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastMetricsUiUpdate = DateTimeOffset.MinValue;

    private PollingRateOption _selectedPollingRate = PollingRateOption.Defaults.First(x => x.Hertz == 1000);
    private ControllerType _previewControllerType = ControllerType.None;
    private string _currentDevice = "未检测到手柄";
    private string _controllerTypeText = "未连接";
    private string _inputModeText = "Auto";
    private string _connectionStatusText = "未连接";
    private string _currentPollingRateText = "1000 Hz";
    private string _actualFrequencyText = "0 Hz";
    private string _averageLatencyText = "0.000 ms";
    private string _exceptionCountText = "0";
    private string _antiStickyStatusText = "待机";
    private string _mappingStatusText = "未开始";
    private ThemeOption _selectedTheme;
    private bool _isDarkTheme;
    private bool _isMappingRunning;
    private bool _isLogExpanded = true;
    private bool _isHotspotCalibrationMode;
    private ControllerState? _pendingUiState;
    private int _uiRefreshScheduled;
    private volatile bool _isWindowMinimized;
    private string _lastPressedUiSignature = "";
    private string _lastDeviceUiSignature = "";
    private ControllerType _lastLayoutControllerType = ControllerType.None;

    public MainViewModel()
    {
        _selectedTheme = _themeService.GetTheme("pink");
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
        NewConfigCommand = new RelayCommand(_ => NewConfig());
        SaveAsConfigCommand = new RelayCommand(_ => SaveAsConfig());
        OpenConfigDirectoryCommand = new RelayCommand(_ => OpenConfigDirectory());
        ResetThemeCommand = new RelayCommand(_ => ResetTheme());
        AddMappingCommand = new RelayCommand(_ => AddMapping());
        DeleteMappingCommand = new RelayCommand(DeleteMapping, x => x is MappingEntry);
        EditMappingCommand = new RelayCommand(EditMapping, x => x is MappingEntry);
        ClearAllMappingsCommand = new RelayCommand(_ => ClearAllMappings(), _ => Mappings.Count > 0);
        SelectSourceButtonCommand = new RelayCommand(SelectSourceButton, x => x is string);
        SelectThemeCommand = new RelayCommand(SelectTheme, x => x is ThemeOption);
        ToggleThemeModeCommand = new RelayCommand(_ => IsDarkTheme = !IsDarkTheme);
        OpenSettingsCommand = new RelayCommand(_ => OpenSettings());
        ToggleHotspotCalibrationCommand = new RelayCommand(_ => IsHotspotCalibrationMode = !IsHotspotCalibrationMode);
        ToggleLogsCommand = new RelayCommand(_ => IsLogExpanded = !IsLogExpanded);
        ClearLogsCommand = new RelayCommand(_ => ClearLogs());
        OpenLogsCommand = new RelayCommand(_ => OpenLogsWindow());
        OpenLogsDirectoryCommand = new RelayCommand(_ => OpenLogsDirectory());
        ExportLogsCommand = new RelayCommand(_ => ExportLogs());
        ClearCacheCommand = new RelayCommand(_ => ClearCache());
        ChangeConfigDirectoryCommand = new RelayCommand(_ => ChangeConfigDirectory());

        Mappings.CollectionChanged += OnMappingsChanged;

        _controllerInputService.StateReceived += OnStateReceived;
        _controllerInputService.MetricsUpdated += OnMetricsUpdated;
        _controllerInputService.Faulted += ex => _mappingEngine.ReleaseAll($"输入线程异常：{ex.Message}");
        _controllerInputService.Log += AddLog;
        _mappingEngine.Log += AddLog;
        _watchdogService.Log += AddLog;

        UpdateLiveButtonLayout(ControllerType.None);
        LoadInitialConfig();
        DetectControllerOnce();

        _deviceMonitorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _deviceMonitorTimer.Tick += (_, _) =>
        {
            if (IsUiRefreshAllowed())
            {
                DetectControllerOnce();
            }
        };
        _deviceMonitorTimer.Start();

        _controllerInputService.Start();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<PollingRateOption> PollingRates => PollingRateOption.Defaults;

    public IReadOnlyList<ThemeOption> Themes => _themeService.Themes;

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
        "PS", "Touchpad",
        "LeftStick", "RightStick",
        "DPadUp", "DPadDown", "DPadLeft", "DPadRight",
        "Button13", "Button14", "Button15", "Button16",
        "Button17", "Button18", "Button19", "Button20",
        "Button21", "Button22", "Button23", "Button24"
    ];

    public ObservableCollection<MappingEntry> Mappings { get; } = new();

    public ObservableCollection<string> PressedButtonNames { get; } = new();

    public IReadOnlyList<string> PressedButtonSnapshot { get; private set; } = Array.Empty<string>();

    public ObservableCollection<LiveButtonIndicator> LiveButtonStates { get; } = new();

    public ObservableCollection<MappingOverviewItem> MappingOverviewItems { get; } = new();

    public ObservableCollection<string> Logs { get; } = new();

    public ICommand StartMappingCommand { get; }

    public ICommand StopMappingCommand { get; }

    public ICommand SaveConfigCommand { get; }

    public ICommand LoadConfigCommand { get; }

    public ICommand NewConfigCommand { get; }

    public ICommand SaveAsConfigCommand { get; }

    public ICommand OpenConfigDirectoryCommand { get; }

    public ICommand ResetThemeCommand { get; }

    public ICommand AddMappingCommand { get; }

    public ICommand DeleteMappingCommand { get; }

    public ICommand EditMappingCommand { get; }

    public ICommand ClearAllMappingsCommand { get; }

    public ICommand SelectSourceButtonCommand { get; }

    public ICommand SelectThemeCommand { get; }

    public ICommand ToggleThemeModeCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public ICommand ToggleHotspotCalibrationCommand { get; }

    public ICommand ToggleLogsCommand { get; }

    public ICommand ClearLogsCommand { get; }

    public ICommand OpenLogsCommand { get; }

    public ICommand OpenLogsDirectoryCommand { get; }

    public ICommand ExportLogsCommand { get; }

    public ICommand ClearCacheCommand { get; }

    public ICommand ChangeConfigDirectoryCommand { get; }

    public int MappingCount => Mappings.Count;

    public string MappingCountText => MappingCount.ToString();

    public int MappingTotalCount => GetRealtimeButtonLayout(PreviewControllerType).Count;

    public string MappingProgressText => $"{MappedSourceButtonCount} / {MappingTotalCount}";

    public int MappedSourceButtonCount => GetRealtimeButtonLayout(PreviewControllerType)
        .Count(item => Mappings.Any(mapping => mapping.Target.IsMapped && ControllerState.SplitAliases(mapping.SourceButton).Contains(item.ButtonId, StringComparer.OrdinalIgnoreCase)));

    public string UnmappedKeyText
    {
        get
        {
            var missing = GetRealtimeButtonLayout(PreviewControllerType)
                .Where(item => !Mappings.Any(mapping => mapping.Target.IsMapped && ControllerState.SplitAliases(mapping.SourceButton).Contains(item.ButtonId, StringComparer.OrdinalIgnoreCase)))
                .Select(item => item.Label)
                .Take(4)
                .ToList();
            return missing.Count == 0 ? "无" : string.Join("、", missing);
        }
    }

    public string LastSavedText { get; private set; } = "本次未保存";

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
            CurrentPollingRateText = value.ShortDisplayName;
            _controllerInputService.SetPollingRate(value.Hertz);
            OnPropertyChanged();
        }
    }

    public ControllerType PreviewControllerType
    {
        get => _previewControllerType;
        set => SetField(ref _previewControllerType, value);
    }

    public ThemeOption SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (value is null || string.Equals(_selectedTheme.Key, value.Key, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _selectedTheme = value;
            _themeService.ApplyTheme(value.Key, IsDarkTheme ? "dark" : "light");
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedControllerAppearanceKey));
        }
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set
        {
            if (!SetField(ref _isDarkTheme, value))
            {
                return;
            }

            _themeService.ApplyTheme(SelectedTheme.Key, value ? "dark" : "light");
            OnPropertyChanged(nameof(ThemeModeText));
            OnPropertyChanged(nameof(ThemeModeIcon));
            OnPropertyChanged(nameof(SelectedControllerAppearanceKey));
        }
    }

    public string ThemeModeText => IsDarkTheme ? "深色" : "浅色";

    public string ThemeModeIcon => IsDarkTheme ? "☾" : "☀";

    public string SelectedControllerAppearanceKey => IsDarkTheme ? "minimal-dark" : "minimal-light";

    public string CurrentProfileName => "默认配置";

    public string ConfigDirectoryText => _configService.ConfigDirectory;

    public string LogsDirectoryText => _configService.LogsDirectory;

    public string CacheDirectoryText => _configService.CacheDirectory;

    public string CacheSizeText => FormatBytes(_configService.GetCacheSizeBytes());

    public string ControllerProtocolText => PreviewControllerType switch
    {
        ControllerType.DualShock4 => "DS4 / HID",
        ControllerType.DualSenseDse => "DSE / HID",
        ControllerType.XInput => "XInput",
        ControllerType.UnknownHid => "HID",
        _ => "Auto"
    };

    public string ControllerLogoPath => PreviewControllerType switch
    {
        ControllerType.XInput => "/Assets/UI/Icons/Xbox.png",
        ControllerType.UnknownHid => "/Assets/UI/Icons/Controller.png",
        _ => "/Assets/UI/Icons/PlayStation.png"
    };

    public string BatteryText => "未读取";

    public bool IsHotspotCalibrationMode
    {
        get => _isHotspotCalibrationMode;
        set
        {
            if (!SetField(ref _isHotspotCalibrationMode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HotspotCalibrationText));
        }
    }

    public string HotspotCalibrationText => IsHotspotCalibrationMode ? "完成校准" : "校准热区";

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

    public bool IsLogExpanded
    {
        get => _isLogExpanded;
        set
        {
            if (!SetField(ref _isLogExpanded, value))
            {
                return;
            }

            OnPropertyChanged(nameof(LogPanelHeight));
            OnPropertyChanged(nameof(LogToggleText));
            OnPropertyChanged(nameof(LogListVisibility));
        }
    }

    public GridLength LogPanelHeight => IsLogExpanded ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;

    public string LogToggleText => IsLogExpanded ? "折叠" : "展开";

    public Visibility LogListVisibility => IsLogExpanded ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MappingOverviewVisibility => MappingOverviewItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MappingEmptyVisibility => MappingOverviewItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void LoadInitialConfig()
    {
        var config = _configService.LoadOrCreate();
        var rate = PollingRates.FirstOrDefault(x => x.Hertz == config.PollingRateHz)
                   ?? PollingRates.First(x => x.Hertz == 1000);
        _selectedPollingRate = rate;
        _selectedTheme = _themeService.GetTheme(config.ThemeKey);
        _isDarkTheme = string.Equals(config.ThemeMode, "dark", StringComparison.OrdinalIgnoreCase);
        _themeService.ApplyTheme(_selectedTheme.Key, _isDarkTheme ? "dark" : "light");
        CurrentPollingRateText = rate.ShortDisplayName;
        _controllerInputService.SetPollingRate(rate.Hertz);
        OnPropertyChanged(nameof(SelectedPollingRate));
        OnPropertyChanged(nameof(SelectedTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(ThemeModeText));
        OnPropertyChanged(nameof(ThemeModeIcon));
        OnPropertyChanged(nameof(SelectedControllerAppearanceKey));
        RefreshMappingOverview();

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
        var signature = $"{info.DeviceName}|{info.ControllerType}|{info.InputMode}|{info.IsConnected}";
        if (string.Equals(_lastDeviceUiSignature, signature, StringComparison.Ordinal))
        {
            return;
        }

        var typeChanged = PreviewControllerType != info.ControllerType;
        _lastDeviceUiSignature = signature;
        CurrentDevice = info.DeviceName;
        ControllerTypeText = info.ControllerType.ToDisplayName();
        InputModeText = info.InputMode;
        ConnectionStatusText = info.IsConnected ? "已连接" : "未连接";
        PreviewControllerType = info.ControllerType;
        OnPropertyChanged(nameof(ControllerProtocolText));
        OnPropertyChanged(nameof(ControllerLogoPath));
        if (typeChanged)
        {
            RefreshMappingOverview();
        }

        UpdateLiveButtonLayout(info.ControllerType);
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
        _mappingEngine.Stop();
        IsMappingRunning = false;
        MappingStatusText = "已停止";
        AntiStickyStatusText = "已释放全部输出";
        AddLog("停止映射，已释放所有按下状态。");
    }

    private void SaveConfig()
    {
        _configService.Save(BuildConfig());
        LastSavedText = DateTime.Now.ToString("HH:mm:ss");
        OnPropertyChanged(nameof(LastSavedText));
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

        MappingConfig config;
        try
        {
            config = _configService.LoadFrom(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导入配置失败：{ex.Message}", "配置导入", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedPollingRate = PollingRates.FirstOrDefault(x => x.Hertz == config.PollingRateHz)
                              ?? PollingRates.First(x => x.Hertz == 1000);
        SelectedTheme = _themeService.GetTheme(config.ThemeKey);
        IsDarkTheme = string.Equals(config.ThemeMode, "dark", StringComparison.OrdinalIgnoreCase);
        Mappings.Clear();
        foreach (var mapping in config.Mappings)
        {
            Mappings.Add(mapping);
        }

        ApplyMappingsToEngine();
        _configService.Save(BuildConfig());
        AddLog($"配置已加载：{dialog.FileName}");
    }

    private void NewConfig()
    {
        _mappingEngine.ReleaseAll("新建配置");
        Mappings.Clear();
        SelectedPollingRate = PollingRates.First(x => x.Hertz == 1000);
        ApplyMappingsToEngine();
        SaveConfig();
        AddLog("已新建空白配置。");
    }

    private void SaveAsConfig()
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出 / 另存为映射配置",
            FileName = "ControllerDog.profile.json",
            Filter = "JSON 配置 (*.json)|*.json|所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _configService.SaveAs(BuildConfig(), dialog.FileName);
        AddLog($"配置已导出：{dialog.FileName}");
    }

    private void OpenConfigDirectory()
    {
        Directory.CreateDirectory(_configService.ConfigDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = _configService.ConfigDirectory,
            UseShellExecute = true
        });
    }

    private void OpenLogsWindow()
    {
        var owner = Application.Current.MainWindow;
        var window = new Window
        {
            Title = "运行日志",
            Owner = owner,
            Width = 780,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
            Background = Application.Current.Resources["BackgroundBrush"] as System.Windows.Media.Brush
        };

        var root = new DockPanel { Margin = new Thickness(18) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 12) };
        var copy = new Button { Content = "复制全部", Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style, Margin = new Thickness(0, 0, 8, 0) };
        copy.Click += (_, _) => Clipboard.SetText(string.Join(Environment.NewLine, Logs));
        var export = new Button { Content = "导出", Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style, Margin = new Thickness(0, 0, 8, 0) };
        export.Click += (_, _) => ExportLogs();
        var openDir = new Button { Content = "打开目录", Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style, Margin = new Thickness(0, 0, 8, 0) };
        openDir.Click += (_, _) => OpenLogsDirectory();
        var clear = new Button { Content = "清空", Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style };
        clear.Click += (_, _) => ClearLogs();
        actions.Children.Add(copy);
        actions.Children.Add(export);
        actions.Children.Add(openDir);
        actions.Children.Add(clear);
        DockPanel.SetDock(actions, Dock.Top);
        root.Children.Add(actions);

        root.Children.Add(new ListBox
        {
            ItemsSource = Logs,
            BorderThickness = new Thickness(1),
            Background = Application.Current.Resources["CardBrush"] as System.Windows.Media.Brush,
            BorderBrush = Application.Current.Resources["BorderBrushSoft"] as System.Windows.Media.Brush,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12,
            Foreground = Application.Current.Resources["TextSecondaryBrush"] as System.Windows.Media.Brush
        });

        window.Content = root;
        window.ShowDialog();
    }

    private void OpenLogsDirectory()
    {
        Directory.CreateDirectory(_configService.LogsDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = _configService.LogsDirectory,
            UseShellExecute = true
        });
    }

    private async void ExportLogs()
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出运行日志",
            FileName = $"抖抖的抖-日志-{DateTime.Now:yyyyMMdd-HHmmss}.log",
            Filter = "日志文件 (*.log)|*.log|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await _configService.ExportLogsAsync(dialog.FileName, Logs);
            AddLog($"日志已导出：{dialog.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出日志失败：{ex.Message}", "日志导出", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ClearLogs()
    {
        Logs.Clear();
        try
        {
            var freed = await _configService.ClearLogFilesAsync();
            AddLog($"日志已清空，释放 {FormatBytes(freed)}。");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"清空日志失败：{ex.Message}", "日志清理", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ClearCache()
    {
        try
        {
            var freed = await _configService.ClearCacheAsync();
            OnPropertyChanged(nameof(CacheSizeText));
            AddLog($"缓存已清理，释放 {FormatBytes(freed)}。");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"清理缓存失败：{ex.Message}", "缓存清理", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ChangeConfigDirectory()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择新的配置保存位置",
            InitialDirectory = _configService.ConfigDirectory
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var migrate = MessageBox.Show("是否迁移现有配置到新目录？", "配置保存位置", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        try
        {
            await _configService.ChangeProfilesDirectoryAsync(dialog.FolderName, migrate);
            OnPropertyChanged(nameof(ConfigDirectoryText));
            AddLog($"配置保存位置已切换：{dialog.FolderName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"修改配置保存位置失败：{ex.Message}", "配置保存位置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AddMapping()
    {
        Mappings.Add(new MappingEntry
        {
            SourceButton = "A",
            Target = new InputTarget(),
            IsEnabled = true
        });
        ApplyMappingsToEngine();
        AddLog("已添加一条映射，点击手柄按键即可重新选择来源和目标。");
    }

    private void ClearAllMappings()
    {
        if (Mappings.Count == 0)
        {
            return;
        }

        _mappingEngine.ReleaseAll("清空全部映射");
        Mappings.Clear();
        ApplyMappingsToEngine();
        AddLog("已清空全部映射。");
        CommandManager.InvalidateRequerySuggested();
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
        AddLog($"已删除映射：{mapping.SourceButton} -> {mapping.Target.DisplayName}");
    }

    private async void EditMapping(object? parameter)
    {
        if (parameter is not MappingEntry mapping)
        {
            return;
        }

        var dialog = new InputCaptureDialog(mapping.Target.Clone(), mapping.SourceButton);
        var result = await dialog.ShowCaptureAsync(Application.Current.MainWindow);

        if (result == true)
        {
            if (dialog.SelectedTarget is null)
            {
                Mappings.Remove(mapping);
                ApplyMappingsToEngine();
                AddLog($"已取消映射：{mapping.SourceButton}");
                return;
            }

            mapping.Target = dialog.SelectedTarget.Clone();
            ApplyMappingsToEngine();
            AddLog($"映射已更新：{mapping.SourceButton} -> {mapping.Target.DisplayName}");
        }
    }

    private async void SelectSourceButton(object? parameter)
    {
        if (parameter is not string sourceButton || string.IsNullOrWhiteSpace(sourceButton))
        {
            return;
        }

        var mapping = FindMappingBySource(sourceButton);
        var isNewMapping = false;
        if (mapping is null)
        {
            mapping = new MappingEntry
            {
                SourceButton = sourceButton,
                Target = new InputTarget(),
                IsEnabled = true
            };
            Mappings.Add(mapping);
            isNewMapping = true;
        }

        var dialog = new InputCaptureDialog(mapping.Target.Clone(), sourceButton);
        var result = await dialog.ShowCaptureAsync(Application.Current.MainWindow);

        if (result == true)
        {
            if (dialog.SelectedTarget is null)
            {
                Mappings.Remove(mapping);
                ApplyMappingsToEngine();
                AddLog($"已取消映射：{sourceButton}");
                return;
            }

            mapping.SourceButton = sourceButton;
            mapping.Target = dialog.SelectedTarget.Clone();
            ApplyMappingsToEngine();
            AddLog($"已设置映射：{mapping.SourceButton} -> {mapping.Target.DisplayName}");
            return;
        }

        if (isNewMapping)
        {
            Mappings.Remove(mapping);
            ApplyMappingsToEngine();
        }
    }

    private MappingEntry? FindMappingBySource(string sourceButton)
    {
        var requestedAliases = ControllerState.SplitAliases(sourceButton);
        return Mappings.FirstOrDefault(mapping =>
        {
            var mappingAliases = ControllerState.SplitAliases(mapping.SourceButton);
            return mappingAliases.Any(alias => requestedAliases.Contains(alias, StringComparer.OrdinalIgnoreCase));
        });
    }

    private void SelectTheme(object? parameter)
    {
        if (parameter is ThemeOption theme)
        {
            SelectedTheme = theme;
            SaveConfig();
        }
    }

    private void ResetTheme()
    {
        SelectedTheme = _themeService.GetTheme("pink");
        IsDarkTheme = false;
        SaveConfig();
    }

    private void OpenSettings()
    {
        var owner = Application.Current.MainWindow;
        var window = new SettingsWindow
        {
            Owner = owner,
            DataContext = this
        };

        window.ShowDialog();
    }

    private void OnStateReceived(object? sender, ControllerState state)
    {
        if (IsUiRefreshAllowed())
        {
            QueueUiRefresh(state);
        }

        _mappingEngine.HandleState(state);
    }

    private void OnMetricsUpdated(object? sender, InputMetrics metrics)
    {
        if (!IsUiRefreshAllowed())
        {
            return;
        }

        var now = DateTimeOffset.Now;
        if ((now - _lastMetricsUiUpdate).TotalMilliseconds < 700)
        {
            return;
        }

        _lastMetricsUiUpdate = now;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            ActualFrequencyText = $"≈ {metrics.ActualFrequencyHz:F0} Hz";
            AverageLatencyText = $"{metrics.AverageLatencyMs:F3} ms";
            ExceptionCountText = metrics.ExceptionCount.ToString();
        });
    }

    private void QueueUiRefresh(ControllerState state)
    {
        var snapshot = state.Clone();
        lock (_pendingUiGate)
        {
            _pendingUiState = snapshot;
        }

        if (Interlocked.Exchange(ref _uiRefreshScheduled, 1) == 0)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(FlushUiRefresh), DispatcherPriority.Input);
        }
    }

    private void FlushUiRefresh()
    {
        if (!IsUiRefreshAllowed())
        {
            lock (_pendingUiGate)
            {
                _pendingUiState = null;
            }

            Interlocked.Exchange(ref _uiRefreshScheduled, 0);
            return;
        }

        ControllerState? snapshot;
        lock (_pendingUiGate)
        {
            snapshot = _pendingUiState;
            _pendingUiState = null;
        }

        if (snapshot is not null)
        {
            UpdateUiState(snapshot);
        }

        Interlocked.Exchange(ref _uiRefreshScheduled, 0);
        lock (_pendingUiGate)
        {
            if (_pendingUiState is not null && Interlocked.Exchange(ref _uiRefreshScheduled, 1) == 0)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(FlushUiRefresh), DispatcherPriority.Input);
            }
        }
    }

    private static bool IsUiRefreshAllowed()
    {
        var app = Application.Current;
        if (app is null)
        {
            return false;
        }

        var dispatcher = app.Dispatcher;
        if (dispatcher is null)
        {
            return false;
        }

        if (!dispatcher.CheckAccess())
        {
            return true;
        }

        var window = app.MainWindow;
        return window is { WindowState: not WindowState.Minimized, IsVisible: true, IsActive: true };
    }

    public void OnWindowStateChanged(WindowState state)
    {
        _isWindowMinimized = state == WindowState.Minimized;
        if (_isWindowMinimized)
        {
            lock (_pendingUiGate)
            {
                _pendingUiState = null;
            }

            Interlocked.Exchange(ref _uiRefreshScheduled, 0);
            return;
        }

        QueueUiRefresh(_controllerInputService.LatestState);
    }

    private void UpdateUiState(ControllerState state)
    {
        var deviceSignature = $"{state.DeviceName}|{state.ControllerType}|{state.InputMode}|{state.IsConnected}";
        if (!string.Equals(_lastDeviceUiSignature, deviceSignature, StringComparison.Ordinal))
        {
            var typeChanged = PreviewControllerType != state.ControllerType;
            _lastDeviceUiSignature = deviceSignature;
            CurrentDevice = state.DeviceName;
            ControllerTypeText = state.ControllerType.ToDisplayName();
            InputModeText = state.InputMode;
            ConnectionStatusText = state.IsConnected ? "已连接" : "未连接";
            PreviewControllerType = state.ControllerType;
            OnPropertyChanged(nameof(ControllerProtocolText));
            OnPropertyChanged(nameof(ControllerLogoPath));
            if (typeChanged)
            {
                RefreshMappingOverview();
            }
        }

        var canonicalButtons = state.GetPressedButtonsSnapshot();
        var pressedSignature = string.Join("|", canonicalButtons.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        var buttonsChanged = !string.Equals(_lastPressedUiSignature, pressedSignature, StringComparison.Ordinal);
        var layoutChanged = UpdateLiveButtonLayout(state.ControllerType);

        if (buttonsChanged)
        {
            _lastPressedUiSignature = pressedSignature;
            PressedButtonSnapshot = canonicalButtons;
            OnPropertyChanged(nameof(PressedButtonSnapshot));
        }

        if (buttonsChanged || layoutChanged)
        {
            var displayButtons = GetDisplayPressedButtons(state);
            var pressed = displayButtons.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var indicator in LiveButtonStates)
            {
                indicator.IsActive = pressed.Contains(indicator.ButtonId);
            }
        }
    }

    private bool UpdateLiveButtonLayout(ControllerType controllerType)
    {
        if (_lastLayoutControllerType == controllerType && LiveButtonStates.Count > 0)
        {
            return false;
        }

        var layout = GetRealtimeButtonLayout(controllerType);
        if (LiveButtonStates.Select(x => x.ButtonId).SequenceEqual(layout.Select(x => x.ButtonId), StringComparer.OrdinalIgnoreCase))
        {
            _lastLayoutControllerType = controllerType;
            return false;
        }

        LiveButtonStates.Clear();
        foreach (var item in layout)
        {
            LiveButtonStates.Add(new LiveButtonIndicator(item.ButtonId, item.Label));
        }

        _lastLayoutControllerType = controllerType;
        return true;
    }

    private static IReadOnlyList<RealtimeButtonItem> GetRealtimeButtonLayout(ControllerType controllerType)
    {
        if (controllerType is ControllerType.DualShock4 or ControllerType.DualSenseDse)
        {
            return
            [
                new("A", "×"),
                new("B", "○"),
                new("X", "□"),
                new("Y", "△"),
                new("DPadUp", "↑"),
                new("DPadDown", "↓"),
                new("DPadLeft", "←"),
                new("DPadRight", "→"),
                new("LB", "L1"),
                new("RB", "R1"),
                new("LT", "L2"),
                new("RT", "R2"),
                new("LeftStick", "L3"),
                new("RightStick", "R3"),
                new("PS", "PS"),
                new("Back", controllerType == ControllerType.DualSenseDse ? "Create" : "Share"),
                new("Start", "Opt"),
                new("Touchpad", "TP")
            ];
        }

        return
        [
            new("A", "A"),
            new("B", "B"),
            new("X", "X"),
            new("Y", "Y"),
            new("DPadUp", "↑"),
            new("DPadDown", "↓"),
            new("DPadLeft", "←"),
            new("DPadRight", "→"),
            new("LB", "LB"),
            new("RB", "RB"),
            new("LT", "LT"),
            new("RT", "RT"),
            new("LeftStick", "LS"),
            new("RightStick", "RS"),
            new("Back", "View"),
            new("Start", "Menu")
        ];
    }

    private static IReadOnlyList<string> GetDisplayPressedButtons(ControllerState state)
    {
        return state.GetPressedButtonsSnapshot()
            .Select(ControllerState.NormalizeAlias)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
    }

    private static string ToControllerDisplayButton(ControllerType controllerType, string button)
    {
        if (controllerType is not (ControllerType.DualShock4 or ControllerType.DualSenseDse))
        {
            return button switch
            {
                "LeftStick" => "LS",
                "RightStick" => "RS",
                "Back" => "View",
                "Start" => "Menu",
                "PS" => "",
                "DPadUp" => "↑",
                "DPadDown" => "↓",
                "DPadLeft" => "←",
                "DPadRight" => "→",
                var value => value
            };
        }

        var mapped = button switch
        {
            "A" => "Cross",
            "B" => "Circle",
            "X" => "Square",
            "Y" => "Triangle",
            "LB" => "L1",
            "RB" => "R1",
            "LT" => "L2",
            "RT" => "R2",
            "Back" => controllerType == ControllerType.DualSenseDse ? "Create" : "Share",
            "Start" => "Options",
            "PS" => "PS",
            _ => button
        };

        return mapped switch
        {
            "Cross" => "×",
            "Circle" => "○",
            "Square" => "□",
            "Triangle" => "△",
            "LeftStick" => controllerType is ControllerType.DualShock4 or ControllerType.DualSenseDse ? "L3" : "LS",
            "RightStick" => controllerType is ControllerType.DualShock4 or ControllerType.DualSenseDse ? "R3" : "RS",
            "Back" => "View",
            "Start" => "Menu",
            "DPadUp" => "↑",
            "DPadDown" => "↓",
            "DPadLeft" => "←",
            "DPadRight" => "→",
            var value => value
        };
    }

    private MappingConfig BuildConfig()
    {
        return new MappingConfig
        {
            PollingRateHz = SelectedPollingRate.Hertz,
            InputMode = "Auto",
            ThemeKey = SelectedTheme.Key,
            ThemeMode = IsDarkTheme ? "dark" : "light",
            ControllerAppearanceKey = "minimal",
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
        CommandManager.InvalidateRequerySuggested();
        OnPropertyChanged(nameof(MappingCount));
        OnPropertyChanged(nameof(MappingCountText));
        RefreshMappingOverview();
    }

    private void OnMappingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_mappingEngine.IsRunning)
        {
            _mappingEngine.ReleaseAll("映射内容修改");
        }

        ApplyMappingsToEngine();
        RefreshMappingOverview();
    }

    private void RefreshMappingOverview()
    {
        MappingOverviewItems.Clear();
        foreach (var mapping in Mappings.Where(x => x.Target.IsMapped).Take(10))
        {
            var label = FormatMappingSource(PreviewControllerType, mapping.SourceButton);
            MappingOverviewItems.Add(new MappingOverviewItem(label, mapping.Target.DisplayName));
        }

        OnPropertyChanged(nameof(MappingTotalCount));
        OnPropertyChanged(nameof(MappingProgressText));
        OnPropertyChanged(nameof(MappedSourceButtonCount));
        OnPropertyChanged(nameof(UnmappedKeyText));
        OnPropertyChanged(nameof(MappingOverviewVisibility));
        OnPropertyChanged(nameof(MappingEmptyVisibility));
    }

    private static string FormatMappingSource(ControllerType controllerType, string sourceButton)
    {
        var labels = ControllerState.SplitAliases(sourceButton)
            .Select(alias => ToControllerDisplayButton(controllerType, alias))
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        return labels.Count == 0 ? sourceButton : string.Join(" / ", labels);
    }

    private void AddLog(string message)
    {
        var now = DateTimeOffset.Now;
        if (_lastLogTimes.TryGetValue(message, out var last) && (now - last).TotalSeconds < 2)
        {
            return;
        }

        _lastLogTimes[message] = now;
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        _ = _configService.AppendLogAsync(line);
        if (_isWindowMinimized)
        {
            return;
        }

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            Logs.Insert(0, line);
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

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:F0} {units[unit]}" : $"{value:F1} {units[unit]}";
    }

    public void Dispose()
    {
        _deviceMonitorTimer.Stop();
        StopMapping();
        _controllerInputService.Dispose();
        _watchdogService.Dispose();
        _mappingEngine.Dispose();
        _hidControllerService.Dispose();
        _inputOutputService.ReleaseAllPhysical();
        try
        {
            Task.Run(() => _configService.RunAutomaticCleanupAsync(isExitCleanup: true)).Wait(250);
        }
        catch
        {
            // Cleanup must never block application exit.
        }
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

public sealed record RealtimeButtonItem(string ButtonId, string Label);

public sealed record MappingOverviewItem(string Source, string Target);

public sealed class LiveButtonIndicator : INotifyPropertyChanged
{
    private bool _isActive;

    public LiveButtonIndicator(string buttonId, string label)
    {
        ButtonId = buttonId;
        Label = label;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ButtonId { get; }

    public string Label { get; }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
            {
                return;
            }

            _isActive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        }
    }
}
