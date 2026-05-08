using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DouDouDeDou.Models;

public sealed class MappingEntry : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _sourceButton = "A";
    private InputTarget _target = new();
    private bool _isEnabled = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string SourceButton
    {
        get => _sourceButton;
        set => SetField(ref _sourceButton, value);
    }

    public InputTarget Target
    {
        get => _target;
        set => SetField(ref _target, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
    }

    public MappingEntry Clone()
    {
        return new MappingEntry
        {
            Id = Id,
            SourceButton = SourceButton,
            Target = Target.Clone(),
            IsEnabled = IsEnabled
        };
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
