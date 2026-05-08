using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace DouDouDeDou.Models;

public enum InputTargetKind
{
    Keyboard,
    Mouse
}

public sealed class InputTarget : INotifyPropertyChanged
{
    private InputTargetKind _kind = InputTargetKind.Keyboard;
    private string _value = "Space";

    public event PropertyChangedEventHandler? PropertyChanged;

    public InputTargetKind Kind
    {
        get => _kind;
        set
        {
            if (_kind == value)
            {
                return;
            }

            _kind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(Signature));
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "Space" : value.Trim();
            if (_value == normalized)
            {
                return;
            }

            _value = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(Signature));
        }
    }

    [JsonIgnore]
    public string DisplayName => Kind == InputTargetKind.Mouse
        ? Value switch
        {
            "LeftButton" => "鼠标左键",
            "RightButton" => "鼠标右键",
            "MiddleButton" => "鼠标中键",
            "XButton1" => "鼠标侧键 1",
            "XButton2" => "鼠标侧键 2",
            _ => Value
        }
        : Value switch
        {
            "Space" => "空格",
            "Enter" => "回车",
            "Escape" => "Esc",
            "LeftShift" => "左 Shift",
            "LeftCtrl" => "左 Ctrl",
            "LeftAlt" => "左 Alt",
            "RightShift" => "右 Shift",
            "RightCtrl" => "右 Ctrl",
            "RightAlt" => "右 Alt",
            _ => $"{Value} 键"
        };

    [JsonIgnore]
    public string Signature => $"{Kind}:{Value}";

    public InputTarget Clone()
    {
        return new InputTarget { Kind = Kind, Value = Value };
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
