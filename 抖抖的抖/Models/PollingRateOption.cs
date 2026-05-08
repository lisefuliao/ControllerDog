namespace DouDouDeDou.Models;

public sealed class PollingRateOption
{
    public int Hertz { get; init; }

    public string DisplayName { get; init; } = "";

    public double IntervalMilliseconds => 1000.0 / Hertz;

    public override string ToString() => DisplayName;

    public static IReadOnlyList<PollingRateOption> Defaults { get; } =
    [
        new() { Hertz = 250, DisplayName = "250 Hz" },
        new() { Hertz = 500, DisplayName = "500 Hz" },
        new() { Hertz = 1000, DisplayName = "1000 Hz" },
        new() { Hertz = 2000, DisplayName = "2000 Hz" },
        new() { Hertz = 4000, DisplayName = "4000 Hz" },
        new() { Hertz = 8000, DisplayName = "8000 Hz（实验）" }
    ];
}
