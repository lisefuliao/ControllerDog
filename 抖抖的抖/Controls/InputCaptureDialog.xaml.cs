using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using DouDouDeDou.Models;
using DouDouDeDou.Utils;

namespace DouDouDeDou.Controls;

public sealed class InputCaptureDialog : Window
{
    private readonly List<Button> _targetButtons = new();
    private readonly Brush _normalButtonBrush = Brushes.White;
    private readonly Brush _selectedButtonBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
    private readonly Brush _normalTextBrush = new SolidColorBrush(Color.FromRgb(32, 42, 56));
    private readonly Brush _selectedTextBrush = Brushes.White;
    private readonly Brush _normalBorderBrush = new SolidColorBrush(Color.FromRgb(215, 224, 236));
    private readonly Brush _selectedBorderBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
    private TextBlock _currentTargetText = null!;
    private Border _captureBanner = null!;
    private bool _isCapturing;
    private bool? _asyncResult;
    private TaskCompletionSource<bool?>? _completionSource;

    public InputCaptureDialog(InputTarget currentTarget)
    {
        SelectedTarget = currentTarget.Clone();

        Title = "选择映射目标";
        Width = 1060;
        Height = 640;
        MinWidth = 940;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        Background = new SolidColorBrush(Color.FromRgb(246, 248, 252));
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/Icons/app-icon.png", UriKind.Absolute));

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += OnPreviewMouseDown;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = BuildHeader();
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var tabs = new TabControl
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 14, 0, 14)
        };
        tabs.Items.Add(new TabItem { Header = "主键盘", Content = WrapPanelCard(BuildKeyboardPanel()) });
        tabs.Items.Add(new TabItem { Header = "功能键 / 数字 / 鼠标", Content = WrapPanelCard(BuildExtendedPanel()) });
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);

        var footer = BuildFooter();
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
        UpdateCurrentText();
        RefreshSelectedHighlights();
    }

    public InputTarget? SelectedTarget { get; private set; }

    public Task<bool?> ShowCaptureAsync(Window? owner)
    {
        Owner = owner;
        _completionSource = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Closed += OnDialogClosed;
        Show();
        Dispatcher.BeginInvoke(() =>
        {
            Activate();
            Keyboard.Focus(this);
        });
        return _completionSource.Task;
    }

    private FrameworkElement BuildHeader()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "设置映射",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(24, 32, 45))
        });
        stack.Children.Add(new TextBlock
        {
            Text = "选择将手柄按键映射到的键盘、鼠标或组合键。",
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(91, 101, 116))
        });

        _currentTargetText = new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0),
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235))
        };
        stack.Children.Add(_currentTargetText);
        grid.Children.Add(stack);

        var capture = CreateActionButton("直接按键捕获", false);
        capture.MinWidth = 138;
        capture.Margin = new Thickness(12, 0, 0, 0);
        capture.Click += OnStartCaptureClick;
        Grid.SetColumn(capture, 1);
        grid.Children.Add(capture);

        return grid;
    }

    private Border WrapPanelCard(UIElement child)
    {
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = _normalBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18),
            Child = child
        };
    }

    private FrameworkElement BuildKeyboardPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(BuildKeyboardRow(("Esc", "Escape", 62), ("F1", "F1", 62), ("F2", "F2", 62), ("F3", "F3", 62), ("F4", "F4", 62), ("F5", "F5", 62), ("F6", "F6", 62), ("F7", "F7", 62), ("F8", "F8", 62), ("F9", "F9", 62), ("F10", "F10", 62), ("F11", "F11", 62), ("F12", "F12", 62)));
        panel.Children.Add(BuildKeyboardRow(("~\n`", "Oem3", 62), ("!\n1", "1", 62), ("@\n2", "2", 62), ("#\n3", "3", 62), ("$\n4", "4", 62), ("%\n5", "5", 62), ("^\n6", "6", 62), ("&\n7", "7", 62), ("*\n8", "8", 62), ("(\n9", "9", 62), (")\n0", "0", 62), ("-\n_", "OemMinus", 62), ("+\n=", "OemPlus", 62), ("Backspace", "Back", 116)));
        panel.Children.Add(BuildKeyboardRow(("Tab", "Tab", 96), ("Q", "Q", 62), ("W", "W", 62), ("E", "E", 62), ("R", "R", 62), ("T", "T", 62), ("Y", "Y", 62), ("U", "U", 62), ("I", "I", 62), ("O", "O", 62), ("P", "P", 62), ("{\n[", "OemOpenBrackets", 62), ("}\n]", "OemCloseBrackets", 62), ("|\n\\", "OemBackslash", 86)));
        panel.Children.Add(BuildKeyboardRow(("Caps", "CapsLock", 112), ("A", "A", 62), ("S", "S", 62), ("D", "D", 62), ("F", "F", 62), ("G", "G", 62), ("H", "H", 62), ("J", "J", 62), ("K", "K", 62), ("L", "L", 62), (":\n;", "OemSemicolon", 62), ("\"\n'", "OemQuotes", 62), ("Enter", "Enter", 130)));
        panel.Children.Add(BuildKeyboardRow(("Shift", "LeftShift", 156), ("Z", "Z", 62), ("X", "X", 62), ("C", "C", 62), ("V", "V", 62), ("B", "B", 62), ("N", "N", 62), ("M", "M", 62), ("<\n,", "OemComma", 62), (">\n.", "OemPeriod", 62), ("?\n/", "OemQuestion", 62), ("Shift", "RightShift", 162)));
        panel.Children.Add(BuildKeyboardRow(("Ctrl", "LeftCtrl", 96), ("Win", "LWin", 82), ("Alt", "LeftAlt", 82), ("Space", "Space", 408), ("Alt", "RightAlt", 82), ("Win", "RWin", 82), ("Apps", "Apps", 82), ("Ctrl", "RightCtrl", 96)));
        return panel;
    }

    private FrameworkElement BuildExtendedPanel()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var nav = new StackPanel();
        nav.Children.Add(BuildKeyboardRow(("Print", "PrintScreen", 62), ("ScrLk", "Scroll", 62), ("Pause", "Pause", 62)));
        nav.Children.Add(BuildKeyboardRow(("Ins", "Insert", 62), ("Home", "Home", 62), ("PgUp", "PageUp", 62)));
        nav.Children.Add(BuildKeyboardRow(("Del", "Delete", 62), ("End", "End", 62), ("PgDn", "PageDown", 62)));
        nav.Children.Add(new Border { Height = 16 });
        nav.Children.Add(BuildKeyboardRow(("↑", "Up", 62)));
        nav.Children.Add(BuildKeyboardRow(("←", "Left", 62), ("↓", "Down", 62), ("→", "Right", 62)));
        root.Children.Add(nav);

        var num = new StackPanel();
        Grid.SetColumn(num, 2);
        num.Children.Add(BuildKeyboardRow(("NumLock", "NumLock", 72), ("Num/", "Divide", 62), ("Num*", "Multiply", 62), ("Num-", "Subtract", 62)));
        num.Children.Add(BuildKeyboardRow(("Num7", "NumPad7", 72), ("Num8", "NumPad8", 62), ("Num9", "NumPad9", 62), ("Num+", "Add", 62)));
        num.Children.Add(BuildKeyboardRow(("Num4", "NumPad4", 72), ("Num5", "NumPad5", 62), ("Num6", "NumPad6", 62)));
        num.Children.Add(BuildKeyboardRow(("Num1", "NumPad1", 72), ("Num2", "NumPad2", 62), ("Num3", "NumPad3", 62), ("Enter", "Enter", 62)));
        num.Children.Add(BuildKeyboardRow(("Num0", "NumPad0", 138), ("Num.", "Decimal", 62)));
        root.Children.Add(num);

        var mouse = new StackPanel();
        Grid.SetColumn(mouse, 4);
        mouse.Children.Add(new TextBlock
        {
            Text = "鼠标按键",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(24, 32, 45)),
            Margin = new Thickness(0, 0, 0, 14)
        });
        mouse.Children.Add(BuildMouseButton("鼠标左键", "LeftButton", 170));
        mouse.Children.Add(BuildMouseButton("鼠标右键", "RightButton", 170));
        mouse.Children.Add(BuildMouseButton("鼠标中键", "MiddleButton", 170));
        mouse.Children.Add(BuildMouseButton("侧键 1", "XButton1", 170));
        mouse.Children.Add(BuildMouseButton("侧键 2", "XButton2", 170));
        root.Children.Add(mouse);

        return root;
    }

    private WrapPanel BuildKeyboardRow(params (string Text, string Value, double Width)[] keys)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var key in keys)
        {
            row.Children.Add(BuildKeyButton(key.Text, key.Value, key.Width));
        }

        return row;
    }

    private Button BuildKeyButton(string text, string value, double width)
    {
        var target = new InputTarget
        {
            Kind = InputTargetKind.Keyboard,
            Value = KeyCodeHelper.NormalizeKeyboardTarget(value)
        };
        var button = BuildTargetButton(text, target, width, 48, new Thickness(0, 0, 7, 0));
        _targetButtons.Add(button);
        ApplySelectedStyle(button, target);
        return button;
    }

    private Button BuildMouseButton(string text, string value, double width)
    {
        var target = new InputTarget { Kind = InputTargetKind.Mouse, Value = value };
        var button = BuildTargetButton(text, target, width, 44, new Thickness(0, 0, 0, 10));
        _targetButtons.Add(button);
        ApplySelectedStyle(button, target);
        return button;
    }

    private Button BuildTargetButton(string text, InputTarget target, double width, double height, Thickness margin)
    {
        var button = new Button
        {
            Content = text,
            Tag = target,
            Width = width,
            Height = height,
            Margin = margin,
            FontSize = 14,
            Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
            Background = _normalButtonBrush,
            BorderBrush = _normalBorderBrush,
            Foreground = _normalTextBrush,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1)
        };
        button.Click += (_, _) => SelectTarget(target.Clone());
        button.MouseEnter += (_, _) => AnimateButton(button, 1.025, 120);
        button.MouseLeave += (_, _) => AnimateButton(button, 1.0, 120);
        button.PreviewMouseLeftButtonDown += (_, _) => AnimateButton(button, 0.965, 90);
        button.PreviewMouseLeftButtonUp += (_, _) => AnimateButton(button, 1.025, 90);
        return button;
    }

    private FrameworkElement BuildFooter()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _captureBanner = new Border
        {
            Padding = new Thickness(14, 9, 14, 9),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromRgb(234, 242, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(193, 215, 255)),
            BorderThickness = new Thickness(1),
            Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _captureBanner.Child = new TextBlock
        {
            Text = "请按下手柄或键盘按键",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235))
        };
        grid.Children.Add(_captureBanner);

        var cancel = CreateActionButton("取消", false);
        cancel.MinWidth = 170;
        cancel.Margin = new Thickness(0, 0, 10, 0);
        cancel.Click += OnCancelClick;
        Grid.SetColumn(cancel, 1);
        grid.Children.Add(cancel);

        var done = CreateActionButton("完成", true);
        done.MinWidth = 190;
        done.Click += OnDoneClick;
        Grid.SetColumn(done, 2);
        grid.Children.Add(done);

        return grid;
    }

    private Button CreateActionButton(string text, bool primary)
    {
        return new Button
        {
            Content = text,
            Height = 40,
            Padding = new Thickness(16, 8, 16, 8),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Style = Application.Current.TryFindResource(primary ? "PrimaryButtonStyle" : "SecondaryButtonStyle") as Style
        };
    }

    private void OnStartCaptureClick(object sender, RoutedEventArgs e)
    {
        _isCapturing = true;
        _captureBanner.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(140)));
        Dispatcher.BeginInvoke(() => Keyboard.Focus(this));
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

    private void SelectTarget(InputTarget target)
    {
        SelectedTarget = target;
        _isCapturing = false;
        _captureBanner.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(140)));
        UpdateCurrentText();
        RefreshSelectedHighlights();
    }

    private void RefreshSelectedHighlights()
    {
        foreach (var button in _targetButtons)
        {
            if (button.Tag is InputTarget target)
            {
                ApplySelectedStyle(button, target);
            }
        }
    }

    private void ApplySelectedStyle(Button button, InputTarget target)
    {
        var selected = SelectedTarget is not null
                       && SelectedTarget.Kind == target.Kind
                       && string.Equals(SelectedTarget.Value, target.Value, StringComparison.OrdinalIgnoreCase);

        button.Background = selected ? _selectedButtonBrush : _normalButtonBrush;
        button.BorderBrush = selected ? _selectedBorderBrush : _normalBorderBrush;
        button.Foreground = selected ? _selectedTextBrush : _normalTextBrush;
        button.FontWeight = selected ? FontWeights.Bold : FontWeights.Normal;
    }

    private void UpdateCurrentText()
    {
        _currentTargetText.Text = SelectedTarget is null
            ? "当前：未选择"
            : $"当前：{SelectedTarget.DisplayName}";
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        CloseWithResult(false);
    }

    private void OnDoneClick(object sender, RoutedEventArgs e)
    {
        CloseWithResult(SelectedTarget is not null);
    }

    private void CloseWithResult(bool result)
    {
        _asyncResult = result;
        Close();
    }

    private void OnDialogClosed(object? sender, EventArgs e)
    {
        Closed -= OnDialogClosed;
        _completionSource?.TrySetResult(_asyncResult ?? false);
    }

    private static void AnimateButton(Button button, double scale, int milliseconds)
    {
        if (button.RenderTransform is not ScaleTransform transform)
        {
            transform = new ScaleTransform(1, 1);
            button.RenderTransform = transform;
        }

        var animation = new DoubleAnimation(scale, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = milliseconds <= 90
                ? new QuadraticEase { EasingMode = EasingMode.EaseOut }
                : new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }
}
