using System.Reflection;
using DouDouDeDou.Models;
using HidSharp;

namespace DouDouDeDou.Services;

public sealed class HidControllerService : IDisposable
{
    private readonly object _gate = new();
    private HidDevice? _device;
    private HidStream? _stream;
    private HidDeviceInfo? _deviceInfo;
    private DateTimeOffset _lastScan = DateTimeOffset.MinValue;
    private HashSet<string> _lastPressedButtons = new(StringComparer.OrdinalIgnoreCase);

    public bool TryReadState(out ControllerState state)
    {
        lock (_gate)
        {
            state = new ControllerState();

            EnsureDevice();
            if (_device is null || _stream is null || _deviceInfo is null)
            {
                return false;
            }

            try
            {
                var buffer = new byte[Math.Max(16, _device.GetMaxInputReportLength())];
                var length = _stream.Read(buffer, 0, buffer.Length);
                if (length > 0)
                {
                    var report = buffer.AsSpan(0, length);
                    _lastPressedButtons = ParseButtons(report, _deviceInfo);
                }

                state = BuildState(length > 0 ? $"HID Report {length} bytes" : "HID 空闲");
                return true;
            }
            catch (TimeoutException)
            {
                // HID 常见行为是“状态变化才发报告”，轮询超时不等于断开。
                state = BuildState("HID 等待新报告");
                return true;
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
        var info = FindBestDeviceInfo();
        if (info is null)
        {
            return null;
        }

        return new ControllerDetectionInfo(
            DeviceName: info.DisplayName,
            ControllerType: info.ControllerType,
            InputMode: "HID",
            IsConnected: true,
            XInputIndex: null,
            VendorId: info.VendorId,
            ProductId: info.ProductId,
            UsagePage: info.UsagePage,
            Usage: info.Usage,
            DevicePath: info.DevicePath,
            Hint: info.MatchReason);
    }

    private ControllerState BuildState(string summary)
    {
        var info = _deviceInfo!;
        return new ControllerState
        {
            IsConnected = true,
            DeviceName = info.DisplayName,
            ControllerType = info.ControllerType,
            InputMode = "HID",
            VendorId = info.VendorId,
            ProductId = info.ProductId,
            UsagePage = info.UsagePage,
            Usage = info.Usage,
            DevicePath = info.DevicePath,
            PressedButtons = new HashSet<string>(_lastPressedButtons, StringComparer.OrdinalIgnoreCase),
            Timestamp = DateTimeOffset.Now,
            RawSummary = summary
        };
    }

    private void EnsureDevice()
    {
        if (_stream is not null && _device is not null && _deviceInfo is not null)
        {
            return;
        }

        if ((DateTimeOffset.Now - _lastScan).TotalMilliseconds < 500)
        {
            return;
        }

        _lastScan = DateTimeOffset.Now;
        var info = FindBestDeviceInfo();
        if (info is null)
        {
            return;
        }

        if (!info.Device.TryOpen(out var stream))
        {
            return;
        }

        stream.ReadTimeout = 1;
        _device = info.Device;
        _stream = stream;
        _deviceInfo = info;
        _lastPressedButtons.Clear();
    }

    private static HidDeviceInfo? FindBestDeviceInfo()
    {
        return DeviceList.Local
            .GetHidDevices()
            .Select(BuildDeviceInfo)
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static HidDeviceInfo BuildDeviceInfo(HidDevice device)
    {
        var productName = SafeGetProductName(device);
        var manufacturer = SafeGetManufacturer(device);
        var devicePath = SafeGetDevicePath(device);
        var usage = DetectUsage(device);
        var controllerType = Classify(device.VendorID, device.ProductID, productName, usage);
        var usageLooksLikeController = usage.IsGameController;
        var knownVidPid = controllerType is ControllerType.DualShock4 or ControllerType.DualSenseDse;

        var score = 0;
        var reason = "未匹配";
        if (usageLooksLikeController)
        {
            score += 80;
            reason = "HID Usage 识别为游戏控制器";
        }

        if (knownVidPid)
        {
            score += 40;
            reason = "Sony VID/PID + HID";
        }

        if (NameLooksLikeController(productName))
        {
            score += 10;
            reason = usageLooksLikeController ? reason : "设备名称像手柄";
        }

        var name = string.IsNullOrWhiteSpace(productName) ? "HID 手柄" : productName;
        if (!string.IsNullOrWhiteSpace(manufacturer) && !name.Contains(manufacturer, StringComparison.OrdinalIgnoreCase))
        {
            name = $"{manufacturer} {name}";
        }

        return new HidDeviceInfo(
            Device: device,
            DisplayName: $"{name} VID_{device.VendorID:X4}&PID_{device.ProductID:X4}",
            ControllerType: controllerType == ControllerType.None ? ControllerType.UnknownHid : controllerType,
            VendorId: device.VendorID,
            ProductId: device.ProductID,
            UsagePage: usage.UsagePage,
            Usage: usage.Usage,
            DevicePath: devicePath,
            Score: score,
            MatchReason: reason);
    }

    private static ControllerType Classify(int vendorId, int productId, string productName, HidUsageInfo usage)
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

        return usage.IsGameController || NameLooksLikeController(name)
            ? ControllerType.UnknownHid
            : ControllerType.None;
    }

    private static HidUsageInfo DetectUsage(HidDevice device)
    {
        try
        {
            var descriptor = device.GetReportDescriptor();
            var deviceItems = GetEnumerableProperty(descriptor, "DeviceItems");
            foreach (var item in deviceItems)
            {
                foreach (var usage in GetEnumerableProperty(item, "Usages"))
                {
                    var page = ReadIntProperty(usage, "Page", "UsagePage");
                    var id = ReadIntProperty(usage, "Id", "Usage", "UsageId");
                    if (page is null || id is null)
                    {
                        continue;
                    }

                    if (page == 0x01 && id is 0x04 or 0x05 or 0x08)
                    {
                        return new HidUsageInfo(page.Value, id.Value, true);
                    }
                }
            }
        }
        catch
        {
            // 有些设备描述符读取会失败，后续仍可走 VID/PID 与名称辅助识别。
        }

        return new HidUsageInfo(null, null, false);
    }

    private static IEnumerable<object> GetEnumerableProperty(object source, string propertyName)
    {
        var value = source.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(source);
        return value is System.Collections.IEnumerable enumerable
            ? enumerable.Cast<object>()
            : [];
    }

    private static int? ReadIntProperty(object source, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            var value = source.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(source);
            if (value is null)
            {
                continue;
            }

            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                // ignore and try next property
            }
        }

        return null;
    }

    private static HashSet<string> ParseButtons(ReadOnlySpan<byte> report, HidDeviceInfo info)
    {
        return info.ControllerType switch
        {
            ControllerType.DualSenseDse => ParseDualSense(report),
            ControllerType.DualShock4 => ParseDualShock4(report),
            _ => ParseGenericController(report)
        };
    }

    private static HashSet<string> ParseDualShock4(ReadOnlySpan<byte> report)
    {
        var pressed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (report.Length < 7)
        {
            return pressed;
        }

        var offset = report[0] is 0x01 or 0x11 ? 1 : 0;
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

    private static bool NameLooksLikeController(string productName)
    {
        return productName.Contains("gamepad", StringComparison.OrdinalIgnoreCase)
               || productName.Contains("controller", StringComparison.OrdinalIgnoreCase)
               || productName.Contains("joystick", StringComparison.OrdinalIgnoreCase)
               || productName.Contains("dualshock", StringComparison.OrdinalIgnoreCase)
               || productName.Contains("dualsense", StringComparison.OrdinalIgnoreCase);
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

    private static string SafeGetManufacturer(HidDevice device)
    {
        try
        {
            return device.GetManufacturer() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string SafeGetDevicePath(HidDevice device)
    {
        try
        {
            return device.GetType().GetProperty("DevicePath", BindingFlags.Instance | BindingFlags.Public)?.GetValue(device)?.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private void CloseStream()
    {
        _stream?.Dispose();
        _stream = null;
        _device = null;
        _deviceInfo = null;
        _lastPressedButtons.Clear();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            CloseStream();
        }
    }

    private sealed record HidUsageInfo(int? UsagePage, int? Usage, bool IsGameController);

    private sealed record HidDeviceInfo(
        HidDevice Device,
        string DisplayName,
        ControllerType ControllerType,
        int VendorId,
        int ProductId,
        int? UsagePage,
        int? Usage,
        string DevicePath,
        int Score,
        string MatchReason);
}
