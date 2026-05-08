using System.Windows;
using System.Windows.Input;
using DouDouDeDou.Models;
using DouDouDeDou.Utils;

namespace DouDouDeDou.Controls;

public partial class InputCaptureDialog : Window
{
    private bool _isCapturing;

    public InputCaptureDialog(InputTarget currentTarget)
    {
        InitializeComponent();
        SelectedTarget = currentTarget.Clone();
        UpdateCurrentText();
    }

    public InputTarget? SelectedTarget { get; private set; }

    private void OnTargetButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string tag)
        {
            SelectTargetFromTag(tag);
        }
    }

    private void OnStartCaptureClick(object sender, RoutedEventArgs e)
    {
        _isCapturing = true;
        CaptureBanner.Opacity = 1.0;
        Keyboard.Focus(this);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isCapturing)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.None)
        {
            return;
        }

        SelectTarget(new InputTarget
        {
            Kind = InputTargetKind.Keyboard,
            Value = KeyCodeHelper.NormalizeKeyboardTarget(key.ToString())
        });
        e.Handled = true;
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isCapturing)
        {
            return;
        }

        var value = e.ChangedButton switch
        {
            MouseButton.Left => "LeftButton",
            MouseButton.Right => "RightButton",
            MouseButton.Middle => "MiddleButton",
            MouseButton.XButton1 => "XButton1",
            MouseButton.XButton2 => "XButton2",
            _ => ""
        };

        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        SelectTarget(new InputTarget { Kind = InputTargetKind.Mouse, Value = value });
        e.Handled = true;
    }

    private void SelectTargetFromTag(string tag)
    {
        var parts = tag.Split(':', 2);
        if (parts.Length != 2 || !Enum.TryParse<InputTargetKind>(parts[0], out var kind))
        {
            return;
        }

        SelectTarget(new InputTarget
        {
            Kind = kind,
            Value = kind == InputTargetKind.Keyboard
                ? KeyCodeHelper.NormalizeKeyboardTarget(parts[1])
                : parts[1]
        });
    }

    private void SelectTarget(InputTarget target)
    {
        SelectedTarget = target;
        _isCapturing = false;
        CaptureBanner.Opacity = 0.0;
        UpdateCurrentText();
    }

    private void UpdateCurrentText()
    {
        CurrentTargetText.Text = SelectedTarget is null
            ? "当前：未选择"
            : $"当前：{SelectedTarget.DisplayName}";
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        DialogResult = SelectedTarget is not null;
        Close();
    }
}
