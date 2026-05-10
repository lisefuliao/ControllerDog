using System.Windows.Media;

namespace DouDouDeDou.Models;

public sealed record ThemeOption(string Key, string DisplayName, Color Color)
{
    public Brush PreviewBrush { get; } = new SolidColorBrush(Color);
}

public sealed record ControllerAppearanceOption(string Key, string DisplayName);
