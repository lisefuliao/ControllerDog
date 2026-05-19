using System.Text.Json.Serialization;

namespace DouDouDeDou.Models;

public sealed class ControllerLayout
{
    public string BaseLight { get; set; } = "";

    public string BaseDark { get; set; } = "";

    public List<ControllerLayoutButton> ButtonItems { get; set; } = [];
}

public sealed class ControllerLayoutButton
{
    public string ButtonId { get; set; } = "";

    public string Label { get; set; } = "";

    public string? HighlightImage { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public double Rotation { get; set; }

    public string Shape { get; set; } = "Circle";

    public string HitTestMode { get; set; } = "Shape";

    public bool IsTemporary { get; set; } = true;

    public int ZIndex { get; set; }

    [JsonIgnore]
    public bool IsRectangle => string.Equals(Shape, "Rectangle", StringComparison.OrdinalIgnoreCase);

    public ControllerLayoutButton Clone()
    {
        return new ControllerLayoutButton
        {
            ButtonId = ButtonId,
            Label = Label,
            HighlightImage = HighlightImage,
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            Rotation = Rotation,
            Shape = Shape,
            HitTestMode = HitTestMode,
            IsTemporary = IsTemporary,
            ZIndex = ZIndex
        };
    }
}
