using System.IO;
using System.Text.Json;
using DouDouDeDou.Models;
using ControllerTypeModel = DouDouDeDou.Models.ControllerType;

namespace DouDouDeDou.Services;

public sealed class ControllerLayoutService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly Dictionary<string, ControllerLayout> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ControllerLayout LoadLayout(ControllerTypeModel controllerType)
    {
        var key = GetControllerKey(controllerType);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var userPath = GetUserLayoutPath(key);
        var projectPath = GetProjectLayoutPath(key);

        try
        {
            var projectLayout = LoadLayoutFile(projectPath);
            if (File.Exists(userPath))
            {
                var userLayout = LoadLayoutFile(userPath);
                cached = ShouldPreferProjectLayout(userLayout, projectLayout) ? projectLayout : userLayout;

                if (ReferenceEquals(cached, projectLayout))
                {
                    TryDeleteUserLayout(userPath);
                }
            }
            else
            {
                cached = projectLayout;
            }
        }
        catch
        {
            cached = new ControllerLayout();
        }

        _cache[key] = cached;
        return cached;
    }

    public void SaveLayout(ControllerTypeModel controllerType, ControllerLayout layout)
    {
        var key = GetControllerKey(controllerType);
        _cache[key] = layout;
        var path = GetUserLayoutPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(layout, JsonOptions));
    }

    public void ResetUserLayout(ControllerTypeModel controllerType)
    {
        var key = GetControllerKey(controllerType);
        _cache.Remove(key);
        var path = GetUserLayoutPath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string GetControllerKey(ControllerTypeModel controllerType)
    {
        return controllerType switch
        {
            ControllerTypeModel.DualSenseDse => "DSE",
            ControllerTypeModel.XInput => "Xbox",
            _ => "Xbox"
        };
    }

    private static string GetProjectLayoutPath(string key)
    {
        return Path.Combine(AppContext.BaseDirectory, "Assets", "Controllers", key, "controller-layout.json");
    }

    private static string GetUserLayoutPath(string key)
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "DouDouDeDou", "Layouts", key, "controller-layout.json");
    }

    private static ControllerLayout LoadLayoutFile(string path)
    {
        if (!File.Exists(path))
        {
            return new ControllerLayout();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ControllerLayout>(json) ?? new ControllerLayout();
    }

    private static bool ShouldPreferProjectLayout(ControllerLayout userLayout, ControllerLayout projectLayout)
    {
        if (projectLayout.ButtonItems.Count == 0)
        {
            return false;
        }

        if (userLayout.ButtonItems.Count == 0)
        {
            return true;
        }

        var projectHasRealOverlays = projectLayout.ButtonItems.Any(item => !string.IsNullOrWhiteSpace(item.HighlightImage));
        if (!projectHasRealOverlays)
        {
            return false;
        }

        return projectLayout.ButtonItems.Any(projectItem =>
        {
            var userItem = userLayout.ButtonItems.FirstOrDefault(item =>
                string.Equals(item.ButtonId, projectItem.ButtonId, StringComparison.OrdinalIgnoreCase));

            return userItem is null
                   || userItem.IsTemporary
                   || string.IsNullOrWhiteSpace(userItem.HighlightImage);
        });
    }

    private static void TryDeleteUserLayout(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // A stale visual layout should not block controller input or startup.
        }
    }
}
