using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using DouDouDeDou.Models;

namespace DouDouDeDou.Services;

public sealed class ConfigService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ConfigDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "抖抖的抖");

    public string UserConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public MappingConfig LoadOrCreate()
    {
        Directory.CreateDirectory(ConfigDirectory);

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
            var backupPath = Path.Combine(ConfigDirectory, $"config.bak.{DateTime.Now:yyyyMMddHHmmss}.json");
            File.Copy(UserConfigPath, backupPath, overwrite: true);
            var config = MappingConfig.CreateDefault();
            Save(config);
            return config;
        }
    }

    public MappingConfig LoadFrom(string path)
    {
        var json = File.ReadAllText(path);
        return Normalize(JsonSerializer.Deserialize<MappingConfig>(json, _jsonOptions) ?? MappingConfig.CreateDefault());
    }

    public void Save(MappingConfig config)
    {
        Directory.CreateDirectory(ConfigDirectory);
        SaveAs(config, UserConfigPath);
    }

    public void SaveAs(MappingConfig config, string path)
    {
        var json = JsonSerializer.Serialize(config, _jsonOptions);
        File.WriteAllText(path, json);
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

    private static MappingConfig Normalize(MappingConfig config)
    {
        config.PollingRateHz = PollingRateOption.Defaults.Any(x => x.Hertz == config.PollingRateHz)
            ? config.PollingRateHz
            : 1000;
        config.InputMode = string.IsNullOrWhiteSpace(config.InputMode) ? "Auto" : config.InputMode;
        config.ThemeKey = string.IsNullOrWhiteSpace(config.ThemeKey) ? "blue" : config.ThemeKey;
        config.ThemeMode = string.IsNullOrWhiteSpace(config.ThemeMode) ? "light" : config.ThemeMode;
        config.ControllerAppearanceKey = string.IsNullOrWhiteSpace(config.ControllerAppearanceKey) ? "minimal" : config.ControllerAppearanceKey;
        config.Mappings ??= new List<MappingEntry>();

        foreach (var mapping in config.Mappings)
        {
            mapping.Id = string.IsNullOrWhiteSpace(mapping.Id) ? Guid.NewGuid().ToString("N") : mapping.Id;
            mapping.SourceButton = string.IsNullOrWhiteSpace(mapping.SourceButton) ? "A" : mapping.SourceButton;
            mapping.Target ??= new InputTarget();
            mapping.Target.Value = string.IsNullOrWhiteSpace(mapping.Target.Value) ? "Space" : mapping.Target.Value;
        }

        return config;
    }
}
