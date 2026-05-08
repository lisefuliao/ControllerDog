namespace DouDouDeDou.Models;

public enum ControllerType
{
    None,
    DualSenseDse,
    DualShock4,
    XInput,
    UnknownHid
}

public static class ControllerTypeExtensions
{
    public static string ToDisplayName(this ControllerType type)
    {
        return type switch
        {
            ControllerType.DualSenseDse => "DualSense / DSE",
            ControllerType.DualShock4 => "DualShock 4 / DS4",
            ControllerType.XInput => "XInput / 类 Xbox 手柄",
            ControllerType.UnknownHid => "未知 HID 手柄",
            _ => "未连接"
        };
    }
}
