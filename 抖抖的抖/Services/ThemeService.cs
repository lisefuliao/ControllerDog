using System.Windows;
using System.Windows.Media;
using DouDouDeDou.Models;

namespace DouDouDeDou.Services;

public sealed class ThemeService
{
    public IReadOnlyList<ThemeOption> Themes { get; } =
    [
        new("pink", "默认粉色", Color.FromRgb(217, 87, 130)),
        new("blue", "蓝色", Color.FromRgb(31, 111, 235)),
        new("purple", "紫色", Color.FromRgb(124, 92, 255)),
        new("green", "绿色", Color.FromRgb(39, 174, 96)),
        new("orange", "橙色", Color.FromRgb(245, 141, 37)),
        new("red", "红色", Color.FromRgb(239, 85, 74)),
        new("gray", "灰色", Color.FromRgb(96, 108, 128)),
        new("black", "深色 / 黑色系", Color.FromRgb(30, 34, 42))
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
        var theme = GetTheme(key);
        var dark = string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(theme.Key, "black", StringComparison.OrdinalIgnoreCase);
        var accent = theme.Color;
        var primary = Blend(accent, dark ? Colors.Black : Colors.White, dark ? 0.08 : 0.14);
        var soft = Blend(accent, dark ? Color.FromRgb(23, 25, 34) : Colors.White, dark ? 0.91 : 0.94);
        var border = Blend(accent, dark ? Color.FromRgb(52, 59, 75) : Color.FromRgb(224, 228, 235), dark ? 0.88 : 0.90);
        var pressed = Blend(accent, Colors.Black, 0.14);

        SetBrush("BackgroundBrush", dark ? Color.FromRgb(10, 12, 17) : Color.FromRgb(244, 246, 249));
        SetBrush("CardBrush", dark ? Color.FromRgb(20, 23, 31) : Color.FromRgb(252, 253, 255));
        SetBrush("PrimaryBrush", primary);
        SetBrush("AccentBrush", accent);
        SetBrush("PressedBrush", pressed);
        SetBrush("SoftPinkBrush", soft);
        SetBrush("BorderBrushSoft", border);
        SetBrush("TextPrimaryBrush", dark ? Color.FromRgb(237, 242, 250) : Color.FromRgb(24, 34, 52));
        SetBrush("TextSecondaryBrush", dark ? Color.FromRgb(157, 168, 184) : Color.FromRgb(104, 115, 134));
        SetBrush("TextMutedBrush", dark ? Color.FromRgb(119, 132, 152) : Color.FromRgb(139, 150, 168));
        SetBrush("IconBrush", dark ? Color.FromRgb(237, 242, 250) : Color.FromRgb(24, 34, 52));
        SetBrush("IconMutedBrush", dark ? Color.FromRgb(157, 168, 184) : Color.FromRgb(104, 115, 134));
        SetBrush("ChipBackgroundBrush", dark ? Color.FromRgb(27, 31, 42) : Color.FromRgb(241, 243, 247));
        SetBrush("ChipPressedBackgroundBrush", accent);
        SetBrush("InputBackgroundBrush", dark ? Color.FromRgb(14, 17, 25) : Color.FromRgb(248, 250, 253));
        SetBrush("SuccessSoftBrush", dark ? Color.FromRgb(26, 61, 45) : Color.FromRgb(232, 248, 239));
        SetBrush("WarningSoftBrush", dark ? Color.FromRgb(68, 49, 27) : Color.FromRgb(255, 243, 228));
        SetBrush("ErrorSoftBrush", dark ? Color.FromRgb(68, 35, 43) : Color.FromRgb(255, 240, 242));
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
