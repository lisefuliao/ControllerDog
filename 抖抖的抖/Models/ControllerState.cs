using System.Collections.ObjectModel;

namespace DouDouDeDou.Models;

public enum ButtonPhase
{
    Up,
    Down,
    Held,
    Released
}

public sealed class ControllerState
{
    public bool IsConnected { get; set; }

    public string DeviceName { get; set; } = "未检测到手柄";

    public ControllerType ControllerType { get; set; } = ControllerType.None;

    public string InputMode { get; set; } = "Auto";

    public int? XInputUserIndex { get; set; }

    public HashSet<string> PressedButtons { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, ButtonPhase> ButtonPhases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    public string RawSummary { get; set; } = "";

    public bool IsPressed(string sourceButton)
    {
        foreach (var alias in SplitAliases(sourceButton))
        {
            if (PressedButtons.Contains(alias))
            {
                return true;
            }
        }

        return false;
    }

    public ReadOnlyCollection<string> GetPressedButtonsSnapshot()
    {
        return PressedButtons
            .Select(NormalizeAlias)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList()
            .AsReadOnly();
    }

    public ControllerState Clone()
    {
        return new ControllerState
        {
            IsConnected = IsConnected,
            DeviceName = DeviceName,
            ControllerType = ControllerType,
            InputMode = InputMode,
            XInputUserIndex = XInputUserIndex,
            PressedButtons = new HashSet<string>(PressedButtons, StringComparer.OrdinalIgnoreCase),
            ButtonPhases = new Dictionary<string, ButtonPhase>(ButtonPhases, StringComparer.OrdinalIgnoreCase),
            Timestamp = Timestamp,
            RawSummary = RawSummary
        };
    }

    public static IReadOnlyList<string> SplitAliases(string sourceButton)
    {
        return sourceButton
            .Split(['/', ',', '，', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeAlias)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string NormalizeAlias(string value)
    {
        var normalized = value.Trim();
        return normalized switch
        {
            "L2" => "LT",
            "R2" => "RT",
            "L1" => "LB",
            "R1" => "RB",
            "Share" => "Back",
            "Create" => "Back",
            "Options" => "Start",
            "Guide" => "PS",
            "Xbox" => "PS",
            "Cross" => "A",
            "Circle" => "B",
            "Square" => "X",
            "Triangle" => "Y",
            "×" => "A",
            "○" => "B",
            "□" => "X",
            "△" => "Y",
            _ => normalized
        };
    }
}
