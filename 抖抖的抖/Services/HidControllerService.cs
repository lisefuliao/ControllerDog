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
                var buffer = new byte[Math.Max(8, _device.GetMaxInputReportLength())];
                var length = _stream.Read(buffer, 0, buffer.Length);
                if (length <= 0)
                {
                    return BuildCurrentState(out state, "HID 空闲");
                }

                var pressed = ParseButtons(buffer.AsSpan(0, length), _controllerType);
                _lastPressedButtons = new HashSet<string>(pressed, StringComparer.OrdinalIgnoreCase);
                state = new ControllerState
                {
                    IsConnected = true,
                    DeviceName = _deviceName,
                    ControllerType = _controllerType,
                    InputMode = "HID",
                    PressedButtons = pressed,
                    Timestamp = DateTimeOffset.Now,
                    RawSummary = $"HID Report {length} bytes"
                };

                return true;
            }
            catch (TimeoutException)
            {
                // HID 读取超时不等于断开，沿用上一次报告，等待设备发送释放报告。
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

        if ((DateTimeOffset.Now - _lastScan).TotalSeconds < 1)
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
            || name.Contains("joystick", StringComparison.OrdinalIgnoreCase))
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
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };
    }

    private static HashSet<string> ParseDualShock4(ReadOnlySpan<byte> report)
    {
        var pressed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (report.Length < 8)
        {
            return pressed;
        }

        var face = report[5];
        AddDPad(face, pressed);
        AddIf((face & 0x10) != 0, "X", "Square", "□", pressed);
        AddIf((face & 0x20) != 0, "A", "Cross", "×", pressed);
        AddIf((face & 0x40) != 0, "B", "Circle", "○", pressed);
        AddIf((face & 0x80) != 0, "Y", "Triangle", "△", pressed);

        var shoulder = report[6];
        AddIf((shoulder & 0x01) != 0, "LB", "L1", pressed);
        AddIf((shoulder & 0x02) != 0, "RB", "R1", pressed);
        AddIf((shoulder & 0x04) != 0, "LT", "L2", pressed);
        AddIf((shoulder & 0x08) != 0, "RT", "R2", pressed);
        AddIf((shoulder & 0x10) != 0, "Back", "Share", pressed);
        AddIf((shoulder & 0x20) != 0, "Start", "Options", pressed);
        AddIf((shoulder & 0x40) != 0, "LeftStick", pressed);
        AddIf((shoulder & 0x80) != 0, "RightStick", pressed);
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
        AddIf((face & 0x10) != 0, "X", "Square", "□", pressed);
        AddIf((face & 0x20) != 0, "A", "Cross", "×", pressed);
        AddIf((face & 0x40) != 0, "B", "Circle", "○", pressed);
        AddIf((face & 0x80) != 0, "Y", "Triangle", "△", pressed);

        var shoulder = report[shoulderIndex];
        AddIf((shoulder & 0x01) != 0, "LB", "L1", pressed);
        AddIf((shoulder & 0x02) != 0, "RB", "R1", pressed);
        AddIf((shoulder & 0x04) != 0, "LT", "L2", pressed);
        AddIf((shoulder & 0x08) != 0, "RT", "R2", pressed);
        AddIf((shoulder & 0x10) != 0, "Back", "Create", pressed);
        AddIf((shoulder & 0x20) != 0, "Start", "Options", pressed);
        return pressed;
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

    private static void AddIf(bool condition, string button, ISet<string> pressed)
    {
        if (condition)
        {
            pressed.Add(button);
        }
    }

    private static void AddIf(bool condition, string button1, string button2, ISet<string> pressed)
    {
        if (condition)
        {
            pressed.Add(button1);
            pressed.Add(button2);
        }
    }

    private static void AddIf(bool condition, string button1, string button2, string button3, ISet<string> pressed)
    {
        if (condition)
        {
            pressed.Add(button1);
            pressed.Add(button2);
            pressed.Add(button3);
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
