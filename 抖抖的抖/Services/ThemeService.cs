using System.Windows;
using System.Windows.Media;
using DouDouDeDou.Models;

namespace DouDouDeDou.Services;

public sealed class ThemeService
{
    public IReadOnlyList<ThemeOption> Themes { get; } =
    [
        new("blue", "蓝色", Color.FromRgb(31, 111, 235)),
        new("green", "绿色", Color.FromRgb(39, 174, 96)),
        new("purple", "紫色", Color.FromRgb(124, 92, 255)),
        new("cyan", "青色", Color.FromRgb(20, 184, 166)),
        new("orange", "橙色", Color.FromRgb(245, 141, 37)),
        new("red", "红色", Color.FromRgb(239, 85, 74)),
        new("pink", "玫红", Color.FromRgb(233, 95, 138))
    ];

    public IReadOnlyList<ControllerAppearanceOption> ControllerAppearances { get; } =
    [
        new("minimal", "简约"),
        new("showcase", "质感")
    ];

    public ThemeOption GetTheme(string? key)
    {
        return Themes.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))
               ?? Themes[0];
    }

    public ControllerAppearanceOption GetControllerAppearance(string? key)
    {
        return ControllerAppearances.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))
               ?? ControllerAppearances[0];
    }

    public void ApplyTheme(string? key, string? mode)
    {
        var dark = string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase);
        var theme = GetTheme(key);
        var accent = theme.Color;
        var primary = Blend(accent, dark ? Colors.Black : Colors.White, dark ? 0.10 : 0.18);
        var soft = Blend(accent, dark ? Color.FromRgb(20, 26, 38) : Colors.White, dark ? 0.78 : 0.88);
        var border = Blend(accent, dark ? Color.FromRgb(55, 65, 82) : Colors.White, dark ? 0.72 : 0.76);
        var pressed = Blend(accent, Colors.Black, 0.14);

        SetBrush("BackgroundBrush", dark ? Color.FromRgb(15, 19, 28) : Color.FromRgb(246, 248, 252));
        SetBrush("CardBrush", dark ? Color.FromRgb(25, 31, 44) : Colors.White);
        SetBrush("PrimaryBrush", primary);
        SetBrush("AccentBrush", accent);
        SetBrush("PressedBrush", pressed);
        SetBrush("SoftPinkBrush", soft);
        SetBrush("BorderBrushSoft", border);
        SetBrush("TextPrimaryBrush", dark ? Color.FromRgb(237, 242, 250) : Color.FromRgb(24, 34, 52));
        SetBrush("TextSecondaryBrush", dark ? Color.FromRgb(157, 168, 184) : Color.FromRgb(104, 115, 134));
        SetBrush("DisabledBrush", dark ? Color.FromRgb(45, 52, 66) : Color.FromRgb(238, 241, 246));
    }

    private static void SetBrush(string key, Color color)
    {
        if (FindResourceDictionary(Application.Current.Resources, key) is not { } dictionary)
        {
            return;
        }

        if (dictionary[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }

        dictionary[key] = new SolidColorBrush(color);
    }

    private static ResourceDictionary? FindResourceDictionary(ResourceDictionary dictionary, string key)
    {
        if (dictionary.Contains(key))
        {
            return dictionary;
        }

        foreach (var merged in dictionary.MergedDictionaries)
        {
            var result = FindResourceDictionary(merged, key);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private static Color Blend(Color left, Color right, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)(left.R + (right.R - left.R) * amount),
            (byte)(left.G + (right.G - left.G) * amount),
            (byte)(left.B + (right.B - left.B) * amount));
    }
}
