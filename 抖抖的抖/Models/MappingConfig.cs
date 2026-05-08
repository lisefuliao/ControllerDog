namespace DouDouDeDou.Models;

public sealed class MappingConfig
{
    public int PollingRateHz { get; set; } = 1000;

    public string InputMode { get; set; } = "Auto";

    public List<MappingEntry> Mappings { get; set; } = new();

    public static MappingConfig CreateDefault()
    {
        return new MappingConfig
        {
            PollingRateHz = 1000,
            InputMode = "Auto",
            Mappings =
            [
                new MappingEntry
                {
                    Id = "default-l2-right-button",
                    SourceButton = "LT / L2",
                    Target = new InputTarget { Kind = InputTargetKind.Mouse, Value = "RightButton" },
                    IsEnabled = true
                },
                new MappingEntry
                {
                    Id = "default-r2-left-button",
                    SourceButton = "RT / R2",
                    Target = new InputTarget { Kind = InputTargetKind.Mouse, Value = "LeftButton" },
                    IsEnabled = true
                },
                new MappingEntry
                {
                    Id = "default-menu-m",
                    SourceButton = "Back / Share / Create",
                    Target = new InputTarget { Kind = InputTargetKind.Keyboard, Value = "M" },
                    IsEnabled = true
                }
            ]
        };
    }
}
