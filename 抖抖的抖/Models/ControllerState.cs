using System.Collections.ObjectModel;

namespace DouDouDeDou.Models;

public sealed class ControllerState
{
    public bool IsConnected { get; set; }

    public string DeviceName { get; set; } = "未检测到手柄";

    public ControllerType ControllerType { get; set; } = ControllerType.None;

    public string InputMode { get; set; } = "Auto";

    public int? XInputUserIndex { get; set; }

    public int? VendorId { get; set; }

    public int? ProductId { get; set; }

    public int? UsagePage { get; set; }

    public int? Usage { get; set; }

    public string DevicePath { get; set; } = "";

    public HashSet<string> PressedButtons { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, ButtonPhase> ButtonPhases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    public string RawSummary { get; set; } = "";

    public string VidPidText
    {
        get
        {
            if (VendorId is null || ProductId is null)
            {
                return "N/A";
            }

            return $"VID_{VendorId.Value:X4} / PID_{ProductId.Value:X4}";
        }
    }

    public string UsageText
    {
        get
        {
            if (UsagePage is null || Usage is null)
            {
                return "N/A";
            }

            return $"0x{UsagePage.Value:X2} / 0x{Usage.Value:X2}";
        }
    }

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
        return PressedButtons.OrderBy(x => x).ToList().AsReadOnly();
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
            VendorId = VendorId,
            ProductId = ProductId,
            UsagePage = UsagePage,
            Usage = Usage,
            DevicePath = DevicePath,
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
