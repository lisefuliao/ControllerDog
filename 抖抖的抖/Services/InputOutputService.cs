using System.Runtime.InteropServices;
using DouDouDeDou.Models;
using DouDouDeDou.Utils;

namespace DouDouDeDou.Services;

public sealed class InputOutputService
{
    private readonly object _gate = new();
    private readonly HashSet<string> _physicalDown = new(StringComparer.OrdinalIgnoreCase);

    public bool Press(InputTarget target)
    {
        lock (_gate)
        {
            if (!_physicalDown.Add(target.Signature))
            {
                return false;
            }
        }

        if (target.Kind == InputTargetKind.Keyboard)
        {
            if (!KeyCodeHelper.TryGetVirtualKey(target.Value, out var virtualKey))
            {
                lock (_gate)
                {
                    _physicalDown.Remove(target.Signature);
                }

                return false;
            }

            var sent = SendKeyboard(virtualKey, keyUp: false);
            if (!sent)
            {
                lock (_gate)
                {
                    _physicalDown.Remove(target.Signature);
                }
            }

            return sent;
        }

        if (TryGetMouseFlags(target.Value, isDown: true, out var flags, out var data))
        {
            var sent = SendMouse(flags, data);
            if (!sent)
            {
                lock (_gate)
                {
                    _physicalDown.Remove(target.Signature);
                }
            }

            return sent;
        }

        lock (_gate)
        {
            _physicalDown.Remove(target.Signature);
        }

        return false;
    }

    public bool Release(InputTarget target)
    {
        lock (_gate)
        {
            if (!_physicalDown.Remove(target.Signature))
            {
                return false;
            }
        }

        if (target.Kind == InputTargetKind.Keyboard)
        {
            var sent = KeyCodeHelper.TryGetVirtualKey(target.Value, out var virtualKey)
                       && SendKeyboard(virtualKey, keyUp: true);
            if (!sent)
            {
                lock (_gate)
                {
                    _physicalDown.Add(target.Signature);
                }
            }

            return sent;
        }

        var mouseSent = TryGetMouseFlags(target.Value, isDown: false, out var flags, out var data)
                        && SendMouse(flags, data);
        if (!mouseSent)
        {
            lock (_gate)
            {
                _physicalDown.Add(target.Signature);
            }
        }

        return mouseSent;
    }

    public void ReleaseAllPhysical()
    {
        List<InputTarget> targets;
        lock (_gate)
        {
            targets = _physicalDown.Select(ParseSignature).Where(x => x is not null).Cast<InputTarget>().ToList();
            _physicalDown.Clear();
        }

        foreach (var target in targets)
        {
            if (target.Kind == InputTargetKind.Keyboard)
            {
                if (KeyCodeHelper.TryGetVirtualKey(target.Value, out var virtualKey))
                {
                    SendKeyboard(virtualKey, keyUp: true);
                }
            }
            else if (TryGetMouseFlags(target.Value, isDown: false, out var flags, out var data))
            {
                SendMouse(flags, data);
            }
        }
    }

    private static InputTarget? ParseSignature(string signature)
    {
        var parts = signature.Split(':', 2);
        if (parts.Length != 2)
        {
            return null;
        }

        return Enum.TryParse<InputTargetKind>(parts[0], ignoreCase: true, out var kind)
            ? new InputTarget { Kind = kind, Value = parts[1] }
            : null;
    }

    private static bool SendKeyboard(ushort virtualKey, bool keyUp)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = 0,
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };

        return SendInput(1, [input], Marshal.SizeOf<INPUT>()) == 1;
    }

    private static bool SendMouse(uint flags, uint data)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = data,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };

        return SendInput(1, [input], Marshal.SizeOf<INPUT>()) == 1;
    }

    private static bool TryGetMouseFlags(string value, bool isDown, out uint flags, out uint data)
    {
        data = 0;
        flags = value switch
        {
            "LeftButton" => isDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP,
            "RightButton" => isDown ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP,
            "MiddleButton" => isDown ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP,
            "XButton1" => isDown ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP,
            "XButton2" => isDown ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP,
            _ => 0
        };

        if (value == "XButton1")
        {
            data = XBUTTON1;
        }
        else if (value == "XButton2")
        {
            data = XBUTTON2;
        }

        return flags != 0;
    }

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_XDOWN = 0x0080;
    private const uint MOUSEEVENTF_XUP = 0x0100;
    private const uint XBUTTON1 = 0x0001;
    private const uint XBUTTON2 = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }
}
