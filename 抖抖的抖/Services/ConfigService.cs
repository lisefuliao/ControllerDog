using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using DouDouDeDou.Models;

namespace DouDouDeDou.Services;

public sealed class ConfigService
{
    private const string AppFolderName = "抖抖的抖";

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public ConfigService()
    {
        DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);
        SettingsPath = Path.Combine(DataDirectory, "settings.json");
        LogsDirectory = Path.Combine(DataDirectory, "logs");
        CacheDirectory = Path.Combine(DataDirectory, "cache");
        TempDirectory = Path.Combine(DataDirectory, "temp");
        DiagnosticsDirectory = Path.Combine(DataDirectory, "diagnostics");
        StorageSettings = LoadStorageSettings();
        EnsureDirectories();
        if (!File.Exists(SettingsPath))
        {
            SaveStorageSettings();
        }

        MigrateLegacyConfigIfNeeded();
    }

    public string DataDirectory { get; }

    public string SettingsPath { get; }

    public string LogsDirectory { get; }

    public string CacheDirectory { get; }

    public string TempDirectory { get; }

    public string DiagnosticsDirectory { get; }

    public AppStorageSettings StorageSettings { get; private set; }

    public string ConfigDirectory => StorageSettings.ProfilesDirectory;

    public string UserConfigPath => Path.Combine(ConfigDirectory, "默认配置.json");

    public string CurrentLogPath => Path.Combine(LogsDirectory, $"app-{DateTime.Now:yyyy-MM-dd}.log");

    public MappingConfig LoadOrCreate()
    {
        EnsureDirectories();

        if (!File.Exists(UserConfigPath))
        {
            var config = Normalize(LoadBundledDefault() ?? MappingConfig.CreateDefault());
            Save(config);
            return config;
        }

        try
        {
            var json = File.ReadAllText(UserConfigPath);
            return Normalize(JsonSerializer.Deserialize<MappingConfig>(json, _jsonOptions) ?? MappingConfig.CreateDefault());
        }
        catch
        {
            var config = MappingConfig.CreateDefault();
            Save(config);
            return config;
        }
    }

    public MappingConfig LoadFrom(string path)
    {
        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<MappingConfig>(json, _jsonOptions)
                     ?? throw new InvalidDataException("配置文件内容为空或格式不正确。");
        return Normalize(config);
    }

    public async Task<MappingConfig> LoadFromAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var config = await JsonSerializer.DeserializeAsync<MappingConfig>(stream, _jsonOptions)
                     ?? throw new InvalidDataException("配置文件内容为空或格式不正确。");
        return Normalize(config);
    }

    public void Save(MappingConfig config)
    {
        EnsureDirectories();
        SaveAs(config, UserConfigPath);
    }

    public void SaveAs(MappingConfig config, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, _jsonOptions);
        File.WriteAllText(path, json);
    }

    public async Task SaveAsAsync(MappingConfig config, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, Normalize(config), _jsonOptions);
    }

    public async Task AppendLogAsync(string line)
    {
        try
        {
            EnsureDirectories();
            await File.AppendAllTextAsync(CurrentLogPath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // Logging must never affect input polling or UI responsiveness.
        }
    }

    public async Task ExportLogsAsync(string destinationPath, IEnumerable<string> inMemoryLogs)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new StringBuilder();
        foreach (var line in inMemoryLogs.Reverse())
        {
            builder.AppendLine(line);
        }

        if (File.Exists(CurrentLogPath))
        {
            builder.AppendLine();
            builder.AppendLine("---- 当前日志文件 ----");
            builder.AppendLine(await File.ReadAllTextAsync(CurrentLogPath, Encoding.UTF8));
        }

        await File.WriteAllTextAsync(destinationPath, builder.ToString(), Encoding.UTF8);
    }

    public async Task<long> ClearLogFilesAsync()
    {
        EnsureDirectories();
        var freed = 0L;
        foreach (var path in Directory.EnumerateFiles(LogsDirectory, "*.log", SearchOption.TopDirectoryOnly))
        {
            freed += await TryDeleteFileAsync(path);
        }

        return freed;
    }

    public async Task<long> ClearCacheAsync()
    {
        EnsureDirectories();
        return await ClearDirectoryContentsAsync(CacheDirectory) + await ClearDirectoryContentsAsync(TempDirectory);
    }

    public long GetCacheSizeBytes()
    {
        EnsureDirectories();
        return GetDirectorySize(CacheDirectory) + GetDirectorySize(TempDirectory);
    }

    public async Task<StorageCleanupResult> RunAutomaticCleanupAsync(bool isExitCleanup)
    {
        EnsureDirectories();
        var freed = 0L;
        var errors = new List<string>();

        if (isExitCleanup && StorageSettings.CleanTempOnExit)
        {
            freed += await ClearDirectoryContentsAsync(TempDirectory, errors);
        }

        if (StorageSettings.LogRetentionDays > 0)
        {
            freed += await DeleteOlderThanAsync(LogsDirectory, "*.log", StorageSettings.LogRetentionDays, keepToday: true, errors);
        }

        freed += await DeleteOlderThanAsync(DiagnosticsDirectory, "*.*", 30, keepToday: false, errors);
        return new StorageCleanupResult(freed, errors);
    }

    public async Task ChangeProfilesDirectoryAsync(string newDirectory, bool migrateExisting)
    {
        if (string.IsNullOrWhiteSpace(newDirectory))
        {
            throw new ArgumentException("配置保存位置不能为空。", nameof(newDirectory));
        }

        var oldDirectory = ConfigDirectory;
        Directory.CreateDirectory(newDirectory);

        if (migrateExisting && Directory.Exists(oldDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(oldDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                var target = Path.Combine(newDirectory, Path.GetFileName(path));
                if (!File.Exists(target))
                {
                    await using var source = File.OpenRead(path);
                    await using var destination = File.Create(target);
                    await source.CopyToAsync(destination);
                }
            }
        }

        StorageSettings.ProfilesDirectory = newDirectory;
        SaveStorageSettings();
        EnsureDirectories();
    }

    public void SetLogRetentionDays(int days)
    {
        StorageSettings.LogRetentionDays = days;
        SaveStorageSettings();
    }

    public void SetCleanTempOnExit(bool clean)
    {
        StorageSettings.CleanTempOnExit = clean;
        SaveStorageSettings();
    }

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(TempDirectory);
        Directory.CreateDirectory(DiagnosticsDirectory);
    }

    private AppStorageSettings LoadStorageSettings()
    {
        var defaultSettings = new AppStorageSettings
        {
            ProfilesDirectory = Path.Combine(DataDirectory, "profiles"),
            LogRetentionDays = 7,
            CleanTempOnExit = true,
            CacheCleanupCycleDays = 30
        };

        if (!File.Exists(SettingsPath))
        {
            return defaultSettings;
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppStorageSettings>(json, _jsonOptions) ?? defaultSettings;
            settings.ProfilesDirectory = string.IsNullOrWhiteSpace(settings.ProfilesDirectory)
                ? defaultSettings.ProfilesDirectory
                : settings.ProfilesDirectory;
            settings.LogRetentionDays = settings.LogRetentionDays is 1 or 3 or 7 or 30 or -1
                ? settings.LogRetentionDays
                : 7;
            settings.CacheCleanupCycleDays = settings.CacheCleanupCycleDays is 1 or 7 or 30 or -1
                ? settings.CacheCleanupCycleDays
                : 30;
            return settings;
        }
        catch
        {
            return defaultSettings;
        }
    }

    private void SaveStorageSettings()
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(StorageSettings, _jsonOptions));
    }

    private void MigrateLegacyConfigIfNeeded()
    {
        var legacy = Path.Combine(DataDirectory, "config.json");
        if (!File.Exists(legacy) || File.Exists(UserConfigPath))
        {
            return;
        }

        Directory.CreateDirectory(ConfigDirectory);
        File.Copy(legacy, UserConfigPath);
    }

    private MappingConfig? LoadBundledDefault()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "config.default.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<MappingConfig>(json, _jsonOptions);
    }

    private async Task<long> ClearDirectoryContentsAsync(string directory, List<string>? errors = null)
    {
        var freed = 0L;
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
        {
            freed += await TryDeleteFileAsync(path, errors);
        }

        foreach (var subDirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(subDirectory).Any())
                {
                    Directory.Delete(subDirectory);
                }
            }
            catch (Exception ex)
            {
                errors?.Add(ex.Message);
            }
        }

        return freed;
    }

    private async Task<long> DeleteOlderThanAsync(string directory, string pattern, int days, bool keepToday, List<string> errors)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var cutoff = DateTime.Now.AddDays(-days);
        var today = DateTime.Today;
        var freed = 0L;
        foreach (var path in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
        {
            var info = new FileInfo(path);
            if (keepToday && info.LastWriteTime.Date >= today)
            {
                continue;
            }

            if (info.LastWriteTime < cutoff)
            {
                freed += await TryDeleteFileAsync(path, errors);
            }
        }

        return freed;
    }

    private static async Task<long> TryDeleteFileAsync(string path, List<string>? errors = null)
    {
        try
        {
            var size = new FileInfo(path).Length;
            await Task.Run(() => File.Delete(path));
            return size;
        }
        catch (Exception ex)
        {
            errors?.Add(ex.Message);
            return 0;
        }
    }

    private static long GetDirectorySize(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        return Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
            .Sum(path =>
            {
                try
                {
                    return new FileInfo(path).Length;
                }
                catch
                {
                    return 0;
                }
            });
    }

    private static MappingConfig Normalize(MappingConfig config)
    {
        config.PollingRateHz = PollingRateOption.Defaults.Any(x => x.Hertz == config.PollingRateHz)
            ? config.PollingRateHz
            : 1000;
        config.InputMode = string.IsNullOrWhiteSpace(config.InputMode) ? "Auto" : config.InputMode;
        config.ThemeKey = string.IsNullOrWhiteSpace(config.ThemeKey) ? "pink" : config.ThemeKey;
        config.ThemeMode = string.IsNullOrWhiteSpace(config.ThemeMode) ? "light" : config.ThemeMode;
        config.ControllerAppearanceKey = string.IsNullOrWhiteSpace(config.ControllerAppearanceKey) ? "minimal" : config.ControllerAppearanceKey;
        config.Mappings ??= new List<MappingEntry>();

        foreach (var mapping in config.Mappings)
        {
            mapping.Id = string.IsNullOrWhiteSpace(mapping.Id) ? Guid.NewGuid().ToString("N") : mapping.Id;
            mapping.SourceButton = string.IsNullOrWhiteSpace(mapping.SourceButton) ? "A" : mapping.SourceButton;
            mapping.Target ??= new InputTarget();
            mapping.Target.Value = string.IsNullOrWhiteSpace(mapping.Target.Value) ? "" : mapping.Target.Value;
        }

        return config;
    }
}

public sealed class AppStorageSettings
{
    public string ProfilesDirectory { get; set; } = "";

    public int LogRetentionDays { get; set; } = 7;

    public bool CleanTempOnExit { get; set; } = true;

    public int CacheCleanupCycleDays { get; set; } = 30;
}

public sealed record StorageCleanupResult(long FreedBytes, IReadOnlyList<string> Errors);
