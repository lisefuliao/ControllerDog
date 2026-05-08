using DouDouDeDou.Models;
using HidSharp;

namespace DouDouDeDou.Services;

public sealed class HidControllerService : IDisposable
{
    private readonly object _gate = new();
    private HidDevice? _device;
    private HidStream? _stream;
    private ControllerType _controllerType = ControllerType.None;
    private string _deviceName = "未检测到 HID 手柄";
    private DateTimeOffset _lastScan = DateTimeOffset.MinValue;
    private HashSet<string> _lastPressedButtons = new(StringComparer.OrdinalIgnoreCase);

    public bool TryReadState(out ControllerState state)
    {
        lock (_gate)
        {
            state = new ControllerState();

            EnsureDevice();
            if (_device is null || _stream is null)
            {
                return false;
            }

            try
            {
                var buffer = new byte[Math.Max(16, _device.GetMaxInputReportLength())];
                var length = _stream.Read(buffer, 0, buffer.Length);
                if (length <= 0)
                {
                    return BuildCurrentState(out state, "HID 空闲");
                }

                _lastPressedButtons = NormalizeButtons(ParseButtons(buffer.AsSpan(0, length), _controllerType));
                return BuildCurrentState(out state, $"HID Report {length} bytes");
            }
            catch (TimeoutException)
            {
                // HID 常见行为是仅在状态变化时发送报告；超时不代表断开，沿用上次稳定状态。
                return BuildCurrentState(out state, "HID 等待新报告");
            }
            catch
            {
                CloseStream();
                return false;
            }
        }
    }

    public ControllerDetectionInfo? DetectFirstHid()
    {
        foreach (var device in DeviceList.Local.GetHidDevices())
        {
            var productName = SafeGetProductName(device);
            var type = Classify(device.VendorID, device.ProductID, productName);
            if (type == ControllerType.None)
            {
                continue;
            }

            return new ControllerDetectionInfo(
                DeviceName: BuildDeviceName(device),
                ControllerType: type,
                InputMode: "HID",
                IsConnected: true,
                XInputIndex: null);
        }

        return null;
    }

    private bool BuildCurrentState(out ControllerState state, string summary)
    {
        state = new ControllerState
        {
            IsConnected = true,
            DeviceName = _deviceName,
            ControllerType = _controllerType,
            InputMode = "HID",
            PressedButtons = new HashSet<string>(_lastPressedButtons, StringComparer.OrdinalIgnoreCase),
            Timestamp = DateTimeOffset.Now,
            RawSummary = summary
        };

        return true;
    }

    private void EnsureDevice()
    {
        if (_stream is not null && _device is not null)
        {
            return;
        }

        if ((DateTimeOffset.Now - _lastScan).TotalMilliseconds < 500)
        {
            return;
        }

        _lastScan = DateTimeOffset.Now;
        foreach (var device in DeviceList.Local.GetHidDevices())
        {
            var productName = SafeGetProductName(device);
            var type = Classify(device.VendorID, device.ProductID, productName);
            if (type == ControllerType.None)
            {
                continue;
            }

            if (!device.TryOpen(out var stream))
            {
                continue;
            }

            stream.ReadTimeout = 1;
            _device = device;
            _stream = stream;
            _controllerType = type;
            _deviceName = BuildDeviceName(device);
            _lastPressedButtons.Clear();
            return;
        }
    }

    private static ControllerType Classify(int vendorId, int productId, string productName)
    {
        var name = productName ?? string.Empty;

        if (vendorId == 0x054C)
        {
            if (productId is 0x0CE6 or 0x0DF2 || name.Contains("DualSense", StringComparison.OrdinalIgnoreCase))
            {
                return ControllerType.DualSenseDse;
            }

            if (productId is 0x05C4 or 0x09CC || name.Contains("Wireless Controller", StringComparison.OrdinalIgnoreCase))
            {
                return ControllerType.DualShock4;
            }
        }

        if (name.Contains("gamepad", StringComparison.OrdinalIgnoreCase)
            || name.Contains("controller", StringComparison.OrdinalIgnoreCase)
            || name.Contains("joystick", StringComparison.OrdinalIgnoreCase)
            || name.Contains("dualshock", StringComparison.OrdinalIgnoreCase)
            || name.Contains("dualsense", StringComparison.OrdinalIgnoreCase))
        {
            return ControllerType.UnknownHid;
        }

        return ControllerType.None;
    }

    private static HashSet<string> ParseButtons(ReadOnlySpan<byte> report, ControllerType type)
    {
        return type switch
        {
            ControllerType.DualSenseDse => ParseDualSense(report),
            ControllerType.DualShock4 => ParseDualShock4(report),
            _ => ParseGenericController(report)
        };
    }

    private static HashSet<string> NormalizeButtons(IEnumerable<string> buttons)
    {
        return buttons
            .Select(ControllerState.NormalizeAlias)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> ParseDualShock4(ReadOnlySpan<byte> report)
    {
        var pressed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (report.Length < 7)
        {
            return pressed;
        }

        var offset = report[0] == 0x11 ? 2 : 0;
        if (report.Length <= offset + 6)
        {
            return pressed;
        }

        var face = report[offset + 5];
        AddDPad(face, pressed);
        AddIf((face & 0x10) != 0, pressed, "X", "Square", "□");
        AddIf((face & 0x20) != 0, pressed, "A", "Cross", "×");
        AddIf((face & 0x40) != 0, pressed, "B", "Circle", "○");
        AddIf((face & 0x80) != 0, pressed, "Y", "Triangle", "△");

        var shoulder = report[offset + 6];
        AddIf((shoulder & 0x01) != 0, pressed, "LB", "L1");
        AddIf((shoulder & 0x02) != 0, pressed, "RB", "R1");
        AddIf((shoulder & 0x04) != 0, pressed, "LT", "L2");
        AddIf((shoulder & 0x08) != 0, pressed, "RT", "R2");
        AddIf((shoulder & 0x10) != 0, pressed, "Back", "Share");
        AddIf((shoulder & 0x20) != 0, pressed, "Start", "Options");
        AddIf((shoulder & 0x40) != 0, pressed, "LeftStick");
        AddIf((shoulder & 0x80) != 0, pressed, "RightStick");

        if (report.Length > offset + 7)
        {
            var system = report[offset + 7];
            AddIf((system & 0x01) != 0, pressed, "PS");
            AddIf((system & 0x02) != 0, pressed, "Touchpad");
        }

        return pressed;
    }

    private static HashSet<string> ParseDualSense(ReadOnlySpan<byte> report)
    {
        var pressed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (report.Length < 10)
        {
            return pressed;
        }

        var faceIndex = report[0] == 0x01 ? 8 : 9;
        var shoulderIndex = faceIndex + 1;
        if (report.Length <= shoulderIndex)
        {
            return pressed;
        }

        var face = report[faceIndex];
        AddDPad(face, pressed);
        AddIf((face & 0x10) != 0, pressed, "X", "Square", "□");
        AddIf((face & 0x20) != 0, pressed, "A", "Cross", "×");
        AddIf((face & 0x40) != 0, pressed, "B", "Circle", "○");
        AddIf((face & 0x80) != 0, pressed, "Y", "Triangle", "△");

        var shoulder = report[shoulderIndex];
        AddIf((shoulder & 0x01) != 0, pressed, "LB", "L1");
        AddIf((shoulder & 0x02) != 0, pressed, "RB", "R1");
        AddIf((shoulder & 0x04) != 0, pressed, "LT", "L2");
        AddIf((shoulder & 0x08) != 0, pressed, "RT", "R2");
        AddIf((shoulder & 0x10) != 0, pressed, "Back", "Create");
        AddIf((shoulder & 0x20) != 0, pressed, "Start", "Options");
        AddIf((shoulder & 0x40) != 0, pressed, "LeftStick");
        AddIf((shoulder & 0x80) != 0, pressed, "RightStick");

        if (report.Length > shoulderIndex + 1)
        {
            var system = report[shoulderIndex + 1];
            AddIf((system & 0x01) != 0, pressed, "PS");
            AddIf((system & 0x02) != 0, pressed, "Touchpad");
        }

        return pressed;
    }

    private static HashSet<string> ParseGenericController(ReadOnlySpan<byte> report)
    {
        var pressed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (report.Length < 3)
        {
            return pressed;
        }

        var buttonBytes = report[^Math.Min(3, report.Length)..];
        var bitIndex = 0;
        foreach (var b in buttonBytes)
        {
            for (var i = 0; i < 8; i++)
            {
                if ((b & (1 << i)) != 0)
                {
                    AddGenericButton(bitIndex, pressed);
                }

                bitIndex++;
            }
        }

        return pressed;
    }

    private static void AddGenericButton(int bitIndex, ISet<string> pressed)
    {
        string[] aliases = bitIndex switch
        {
            0 => ["A"],
            1 => ["B"],
            2 => ["X"],
            3 => ["Y"],
            4 => ["LB", "L1"],
            5 => ["RB", "R1"],
            6 => ["LT", "L2"],
            7 => ["RT", "R2"],
            8 => ["Back", "Share", "Create"],
            9 => ["Start", "Options"],
            10 => ["LeftStick"],
            11 => ["RightStick"],
            _ => [$"Button{bitIndex + 1}"]
        };

        foreach (var alias in aliases)
        {
            pressed.Add(alias);
        }
    }

    private static void AddDPad(byte face, ISet<string> pressed)
    {
        switch (face & 0x0F)
        {
            case 0:
                pressed.Add("DPadUp");
                break;
            case 1:
                pressed.Add("DPadUp");
                pressed.Add("DPadRight");
                break;
            case 2:
                pressed.Add("DPadRight");
                break;
            case 3:
                pressed.Add("DPadRight");
                pressed.Add("DPadDown");
                break;
            case 4:
                pressed.Add("DPadDown");
                break;
            case 5:
                pressed.Add("DPadDown");
                pressed.Add("DPadLeft");
                break;
            case 6:
                pressed.Add("DPadLeft");
                break;
            case 7:
                pressed.Add("DPadLeft");
                pressed.Add("DPadUp");
                break;
        }
    }

    private static void AddIf(bool condition, ISet<string> pressed, params string[] names)
    {
        if (!condition)
        {
            return;
        }

        foreach (var name in names)
        {
            pressed.Add(name);
        }
    }

    private static string SafeGetProductName(HidDevice device)
    {
        try
        {
            return device.GetProductName() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string BuildDeviceName(HidDevice device)
    {
        var productName = SafeGetProductName(device);
        var name = string.IsNullOrWhiteSpace(productName) ? "HID 手柄" : productName;
        return $"{name} VID_{device.VendorID:X4}&PID_{device.ProductID:X4}";
    }

    private void CloseStream()
    {
        _stream?.Dispose();
        _stream = null;
        _device = null;
        _controllerType = ControllerType.None;
        _deviceName = "未检测到 HID 手柄";
        _lastPressedButtons.Clear();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            CloseStream();
        }
    }
}
