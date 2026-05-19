namespace DouDouDeDou.Models;

public sealed class PollingRateOption
{
    public int Hertz { get; init; }

    public string DisplayName { get; init; } = "";

    public string DropDownDisplayName { get; init; } = "";

    public string ShortDisplayName => $"{Hertz} Hz";

    public double IntervalMilliseconds => 1000.0 / Hertz;

    public override string ToString() => ShortDisplayName;

    public static IReadOnlyList<PollingRateOption> Defaults { get; } =
    [
        new() { Hertz = 250, DisplayName = "250 Hz" },
        new() { Hertz = 500, DisplayName = "500 Hz" },
        new() { Hertz = 1000, DisplayName = "1000 Hz" },
        new() { Hertz = 2000, DisplayName = "2000 Hz" },
        new() { Hertz = 4000, DisplayName = "4000 Hz" },
        new() { Hertz = 8000, DisplayName = "8000 Hz", DropDownDisplayName = "8000 Hz（有点拿命竞赛了吧）" }
    ];

    public string ListDisplayName => string.IsNullOrWhiteSpace(DropDownDisplayName)
        ? DisplayName
        : DropDownDisplayName;
}
