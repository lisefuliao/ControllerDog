using System.Windows.Input;

namespace DouDouDeDou.Utils;

public static class KeyCodeHelper
{
    private static readonly Dictionary<string, Key> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["空格"] = Key.Space,
        ["Space"] = Key.Space,
        ["回车"] = Key.Enter,
        ["Enter"] = Key.Enter,
        ["Esc"] = Key.Escape,
        ["Escape"] = Key.Escape,
        ["左Shift"] = Key.LeftShift,
        ["右Shift"] = Key.RightShift,
        ["左Ctrl"] = Key.LeftCtrl,
        ["右Ctrl"] = Key.RightCtrl,
        ["左Alt"] = Key.LeftAlt,
        ["右Alt"] = Key.RightAlt,
        ["上"] = Key.Up,
        ["下"] = Key.Down,
        ["左"] = Key.Left,
        ["右"] = Key.Right,
        [";"] = Key.OemSemicolon,
        ["="] = Key.OemPlus,
        [","] = Key.OemComma,
        ["-"] = Key.OemMinus,
        ["."] = Key.OemPeriod,
        ["/"] = Key.OemQuestion,
        ["`"] = Key.Oem3,
        ["["] = Key.OemOpenBrackets,
        ["\\"] = Key.OemPipe,
        ["]"] = Key.OemCloseBrackets,
        ["'"] = Key.OemQuotes
    };

    public static IReadOnlyList<string> KeyboardTargets { get; } = BuildKeyboardTargets();

    public static IReadOnlyList<string> MouseTargets { get; } =
    [
        "LeftButton",
        "RightButton",
        "MiddleButton",
        "XButton1",
        "XButton2"
    ];

    public static bool TryGetVirtualKey(string value, out ushort virtualKey)
    {
        virtualKey = 0;
        var normalized = value.Trim();

        if (Aliases.TryGetValue(normalized, out var aliasKey))
        {
            virtualKey = (ushort)KeyInterop.VirtualKeyFromKey(aliasKey);
            return virtualKey != 0;
        }

        if (normalized.Length == 1)
        {
            var c = normalized[0];
            if (c is >= 'A' and <= 'Z' || c is >= 'a' and <= 'z')
            {
                normalized = char.ToUpperInvariant(c).ToString();
            }
            else if (c is >= '0' and <= '9')
            {
                normalized = $"D{c}";
            }
        }

        if (Enum.TryParse<Key>(normalized, ignoreCase: true, out var key))
        {
            virtualKey = (ushort)KeyInterop.VirtualKeyFromKey(key);
            return virtualKey != 0;
        }

        return false;
    }

    public static string NormalizeKeyboardTarget(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Space";
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 1 && char.IsLetter(trimmed[0]))
        {
            return char.ToUpperInvariant(trimmed[0]).ToString();
        }

        return trimmed;
    }

    private static IReadOnlyList<string> BuildKeyboardTargets()
    {
        var result = new List<string>();
        result.AddRange("ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(x => x.ToString()));
        result.AddRange(Enumerable.Range(0, 10).Select(x => x.ToString()));
        result.AddRange(["Space", "Enter", "Escape", "Tab", "Back", "Delete", "Insert", "Home", "End", "PageUp", "PageDown"]);
        result.AddRange(["Up", "Down", "Left", "Right", "LeftShift", "LeftCtrl", "LeftAlt", "RightShift", "RightCtrl", "RightAlt"]);
        result.AddRange(["CapsLock", "NumLock", "Scroll", "PrintScreen", "Pause"]);
        result.AddRange(["OemSemicolon", "OemPlus", "OemComma", "OemMinus", "OemPeriod", "OemQuestion", "Oem3", "OemOpenBrackets", "OemPipe", "OemCloseBrackets", "OemQuotes"]);
        result.AddRange(Enumerable.Range(1, 24).Select(x => $"F{x}"));
        return result;
    }
}
