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

    public MappingEntry()
    {
        _target.PropertyChanged += OnTargetPropertyChanged;
    }

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
        set
        {
            if (ReferenceEquals(_target, value))
            {
                return;
            }

            _target.PropertyChanged -= OnTargetPropertyChanged;
            _target = value;
            _target.PropertyChanged += OnTargetPropertyChanged;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Target)));
            RaiseTargetDisplayChanges();
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
    }

    public string TargetDisplayName => Target.DisplayName;

    public string OutputTypeDisplayName => Target.Kind == InputTargetKind.Mouse ? "鼠标" : "键盘";

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

    private void OnTargetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaiseTargetDisplayChanges();
    }

    private void RaiseTargetDisplayChanges()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TargetDisplayName)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OutputTypeDisplayName)));
    }
}
