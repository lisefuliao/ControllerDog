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
    private string _value = "";

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
            var normalized = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
            if (_value == normalized)
            {
                return;
            }

            _value = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMapped));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(Signature));
        }
    }

    [JsonIgnore]
    public bool IsMapped => !string.IsNullOrWhiteSpace(Value);

    [JsonIgnore]
    public string DisplayName => !IsMapped
        ? "未映射"
        : Kind == InputTargetKind.Mouse
        ? Value switch
        {
            "LeftButton" => "鼠标左键",
            "RightButton" => "鼠标右键",
            "MiddleButton" => "鼠标中键",
            "XButton1" => "鼠标侧键 1",
            "XButton2" => "鼠标侧键 2",
            _ => Value
        }
        : $"{Value} 键";

    [JsonIgnore]
    public string Signature => IsMapped ? $"{Kind}:{Value}" : "Unmapped";

    public InputTarget Clone()
    {
        return new InputTarget { Kind = Kind, Value = Value };
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
