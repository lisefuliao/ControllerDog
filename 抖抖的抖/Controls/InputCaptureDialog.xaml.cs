using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DouDouDeDou.Models;
using DouDouDeDou.Utils;

namespace DouDouDeDou.Controls;

public sealed class InputCaptureDialog : Window
{
    private readonly List<Button> _targetButtons = new();
    private readonly string _sourceButtonName;
    private readonly Brush _normalButtonBrush = new SolidColorBrush(Color.FromRgb(251, 252, 254));
    private readonly Brush _selectedButtonBrush = new SolidColorBrush(Color.FromRgb(217, 87, 130));
    private readonly Brush _normalTextBrush = new SolidColorBrush(Color.FromRgb(32, 42, 56));
    private readonly Brush _selectedTextBrush = Brushes.White;
    private readonly Brush _normalBorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236));
    private readonly Brush _selectedBorderBrush = new SolidColorBrush(Color.FromRgb(217, 87, 130));
    private TextBlock _currentTargetText = null!;
    private bool? _asyncResult;
    private TaskCompletionSource<bool?>? _completionSource;

    public InputCaptureDialog(InputTarget? currentTarget, string sourceButtonName = "")
    {
        _sourceButtonName = string.IsNullOrWhiteSpace(sourceButtonName) ? "当前按键" : sourceButtonName;
        SelectedTarget = currentTarget?.IsMapped == true ? currentTarget.Clone() : null;

        Title = "设置映射";
        Width = 1000;
        Height = 660;
        MinWidth = 940;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        Background = new SolidColorBrush(Color.FromRgb(245, 246, 248));
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/Branding/AppIcon.png", UriKind.Absolute));
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        FocusVisualStyle = null;

        PreviewKeyDown += OnPreviewKeyDown;

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = BuildHeader();
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var body = WrapPanelCard(BuildUnifiedChooser());
        body.Margin = new Thickness(0, 16, 0, 16);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

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
            Focus();
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
            Text = "键盘直接按下，鼠标只点右侧区域。",
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(91, 101, 116))
        });

        var sourcePill = new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(12, 7, 12, 7),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromRgb(255, 241, 245)),
            BorderBrush = _normalBorderBrush,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = $"手柄按键：{_sourceButtonName}",
                FontWeight = FontWeights.SemiBold,
                Foreground = _selectedButtonBrush
            }
        };
        stack.Children.Add(sourcePill);

        _currentTargetText = new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0),
            FontWeight = FontWeights.SemiBold,
            Foreground = _selectedButtonBrush
        };
        stack.Children.Add(_currentTargetText);
        grid.Children.Add(stack);

        var state = new Border
        {
            Padding = new Thickness(14, 9, 14, 9),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromRgb(251, 252, 254)),
            BorderBrush = _normalBorderBrush,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = "监听中",
                FontWeight = FontWeights.Bold,
                Foreground = _selectedButtonBrush
            }
        };
        Grid.SetColumn(state, 1);
        grid.Children.Add(state);

        return grid;
    }

    private Border WrapPanelCard(UIElement child)
    {
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = _normalBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(18),
            Child = child
        };
    }

    private FrameworkElement BuildUnifiedChooser()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });

        var keyboard = new StackPanel();
        keyboard.Children.Add(new TextBlock
        {
            Text = "选择目标输入",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(24, 32, 45)),
            Margin = new Thickness(0, 0, 0, 14)
        });
        keyboard.Children.Add(BuildKeyboardRow(("Esc", "Escape", 54), ("F1", "F1", 54), ("F2", "F2", 54), ("F3", "F3", 54), ("F4", "F4", 54), ("F5", "F5", 54), ("F6", "F6", 54), ("F7", "F7", 54), ("F8", "F8", 54), ("F9", "F9", 54), ("F10", "F10", 58), ("F11", "F11", 58), ("F12", "F12", 58)));
        keyboard.Children.Add(BuildKeyboardRow(("~\n`", "Oem3", 54), ("!\n1", "1", 54), ("@\n2", "2", 54), ("#\n3", "3", 54), ("$\n4", "4", 54), ("%\n5", "5", 54), ("^\n6", "6", 54), ("&\n7", "7", 54), ("*\n8", "8", 54), ("(\n9", "9", 54), (")\n0", "0", 54), ("-\n_", "OemMinus", 54), ("+\n=", "OemPlus", 54), ("Back", "Back", 86)));
        keyboard.Children.Add(BuildKeyboardRow(("Tab", "Tab", 76), ("Q", "Q", 54), ("W", "W", 54), ("E", "E", 54), ("R", "R", 54), ("T", "T", 54), ("Y", "Y", 54), ("U", "U", 54), ("I", "I", 54), ("O", "O", 54), ("P", "P", 54), ("{\n[", "OemOpenBrackets", 54), ("}\n]", "OemCloseBrackets", 54), ("|\n\\", "OemBackslash", 70)));
        keyboard.Children.Add(BuildKeyboardRow(("Caps", "CapsLock", 90), ("A", "A", 54), ("S", "S", 54), ("D", "D", 54), ("F", "F", 54), ("G", "G", 54), ("H", "H", 54), ("J", "J", 54), ("K", "K", 54), ("L", "L", 54), (":\n;", "OemSemicolon", 54), ("\"\n'", "OemQuotes", 54), ("Enter", "Enter", 104)));
        keyboard.Children.Add(BuildKeyboardRow(("Shift", "LeftShift", 120), ("Z", "Z", 54), ("X", "X", 54), ("C", "C", 54), ("V", "V", 54), ("B", "B", 54), ("N", "N", 54), ("M", "M", 54), ("<\n,", "OemComma", 54), (">\n.", "OemPeriod", 54), ("?\n/", "OemQuestion", 54), ("Shift", "RightShift", 126)));
        keyboard.Children.Add(BuildKeyboardRow(("Ctrl", "LeftCtrl", 76), ("Win", "LWin", 66), ("Alt", "LeftAlt", 66), ("Space", "Space", 320), ("Alt", "RightAlt", 66), ("Win", "RWin", 66), ("Apps", "Apps", 66), ("Ctrl", "RightCtrl", 76)));
        keyboard.Children.Add(BuildKeyboardRow(("Ins", "Insert", 54), ("Home", "Home", 62), ("PgUp", "PageUp", 62), ("Del", "Delete", 54), ("End", "End", 62), ("PgDn", "PageDown", 62), ("↑", "Up", 54), ("←", "Left", 54), ("↓", "Down", 54), ("→", "Right", 54)));
        root.Children.Add(keyboard);

        var mouse = new StackPanel { Margin = new Thickness(18, 34, 0, 0) };
        Grid.SetColumn(mouse, 1);
        mouse.Children.Add(BuildMousePad());
        mouse.Children.Add(BuildMouseButton("左键", "LeftButton"));
        mouse.Children.Add(BuildMouseButton("右键", "RightButton"));
        mouse.Children.Add(BuildMouseButton("中键", "MiddleButton"));
        mouse.Children.Add(BuildMouseButton("侧键 1", "XButton1"));
        mouse.Children.Add(BuildMouseButton("侧键 2", "XButton2"));
        root.Children.Add(mouse);

        return root;
    }

    private FrameworkElement BuildMousePad()
    {
        var pad = new Border
        {
            Height = 174,
            CornerRadius = new CornerRadius(34),
            Background = new SolidColorBrush(Color.FromRgb(247, 248, 251)),
            BorderBrush = _normalBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 14)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });
        grid.RowDefinitions.Add(new RowDefinition());
        var left = BuildMouseGraphicButton("左键", "LeftButton", new CornerRadius(20, 8, 8, 12));
        grid.Children.Add(left);
        var right = BuildMouseGraphicButton("右键", "RightButton", new CornerRadius(8, 20, 12, 8));
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        var wheel = BuildMouseGraphicButton("", "MiddleButton", new CornerRadius(8));
        wheel.Width = 14;
        wheel.Height = 40;
        wheel.HorizontalAlignment = HorizontalAlignment.Center;
        wheel.VerticalAlignment = VerticalAlignment.Top;
        wheel.Margin = new Thickness(0, 10, 0, 0);
        wheel.Background = _selectedButtonBrush;
        Grid.SetColumn(wheel, 1);
        grid.Children.Add(wheel);
        var body = new Border { CornerRadius = new CornerRadius(12, 12, 24, 24), Background = Brushes.White, BorderBrush = _normalBorderBrush, BorderThickness = new Thickness(1), Margin = new Thickness(0, 8, 0, 0), IsHitTestVisible = false };
        Grid.SetRow(body, 1);
        Grid.SetColumnSpan(body, 3);
        grid.Children.Add(body);
        var side1 = BuildMouseGraphicButton("侧 1", "XButton1", new CornerRadius(8, 14, 14, 8));
        side1.Width = 52;
        side1.Height = 28;
        side1.HorizontalAlignment = HorizontalAlignment.Left;
        side1.VerticalAlignment = VerticalAlignment.Top;
        side1.Margin = new Thickness(6, 18, 0, 0);
        Grid.SetRow(side1, 1);
        grid.Children.Add(side1);
        var side2 = BuildMouseGraphicButton("侧 2", "XButton2", new CornerRadius(8, 14, 14, 8));
        side2.Width = 52;
        side2.Height = 28;
        side2.HorizontalAlignment = HorizontalAlignment.Left;
        side2.VerticalAlignment = VerticalAlignment.Top;
        side2.Margin = new Thickness(6, 52, 0, 0);
        Grid.SetRow(side2, 1);
        grid.Children.Add(side2);
        pad.Child = grid;
        return pad;
    }

    private Button BuildMouseGraphicButton(string text, string value, CornerRadius cornerRadius)
    {
        var target = new InputTarget { Kind = InputTargetKind.Mouse, Value = value };
        var button = new Button
        {
            Content = text,
            Tag = target,
            Background = Brushes.White,
            BorderBrush = _normalBorderBrush,
            BorderThickness = new Thickness(1),
            Foreground = _normalTextBrush,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            FocusVisualStyle = null,
            Template = CreateMouseRegionTemplate(cornerRadius)
        };
        button.Click += (_, _) => SelectTarget(target.Clone());
        button.PreviewMouseRightButtonDown += (_, e) =>
        {
            ClearSelectedTarget();
            e.Handled = true;
        };
        _targetButtons.Add(button);
        ApplySelectedStyle(button, target);
        return button;
    }

    private static ControlTemplate CreateMouseRegionTemplate(CornerRadius cornerRadius)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Root";
        border.SetValue(Border.CornerRadiusProperty, cornerRadius);
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(255, 228, 225)), "Root"));
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(217, 87, 130)), "Root"));
        template.Triggers.Add(hover);
        return template;
    }

    private WrapPanel BuildKeyboardRow(params (string Text, string Value, double Width)[] keys)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
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
        var button = BuildTargetButton(text, target, width, 42, new Thickness(0, 0, 7, 0));
        button.Style = Application.Current.TryFindResource("KeyboardKeyButtonStyle") as Style;
        _targetButtons.Add(button);
        ApplySelectedStyle(button, target);
        return button;
    }

    private Button BuildMouseButton(string text, string value)
    {
        var target = new InputTarget { Kind = InputTargetKind.Mouse, Value = value };
        var button = BuildTargetButton(text, target, 220, 42, new Thickness(0, 0, 0, 9));
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
            FontSize = 13,
            Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
            Background = _normalButtonBrush,
            BorderBrush = _normalBorderBrush,
            Foreground = _normalTextBrush,
            FocusVisualStyle = null
        };
        button.Click += (_, _) => SelectTarget(target.Clone());
        button.PreviewMouseRightButtonDown += (_, e) =>
        {
            ClearSelectedTarget();
            e.Handled = true;
        };
        return button;
    }

    private FrameworkElement BuildFooter()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new TextBlock
        {
            Text = "Delete / Backspace 清空当前映射；右键已选鼠标项可取消。",
            Foreground = new SolidColorBrush(Color.FromRgb(91, 101, 116)),
            VerticalAlignment = VerticalAlignment.Center
        });

        var clear = CreateActionButton("清空当前映射", false);
        clear.Style = Application.Current.TryFindResource("DangerOutlineButtonStyle") as Style;
        clear.MinWidth = 140;
        clear.Margin = new Thickness(0, 0, 10, 0);
        clear.Click += (_, _) => ClearSelectedTarget();
        Grid.SetColumn(clear, 1);
        grid.Children.Add(clear);

        var cancel = CreateActionButton("取消", false);
        cancel.MinWidth = 112;
        cancel.Margin = new Thickness(0, 0, 10, 0);
        cancel.Click += OnCancelClick;
        Grid.SetColumn(cancel, 2);
        grid.Children.Add(cancel);

        var done = CreateActionButton("完成", true);
        done.MinWidth = 128;
        done.Click += OnDoneClick;
        Grid.SetColumn(done, 3);
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
            FocusVisualStyle = null,
            Style = Application.Current.TryFindResource(primary ? "PrimaryButtonStyle" : "SecondaryButtonStyle") as Style
        };
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            CloseWithResult(false);
            e.Handled = true;
            return;
        }

        if (key is Key.Delete or Key.Back)
        {
            ClearSelectedTarget();
            e.Handled = true;
            return;
        }

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

    private void SelectTarget(InputTarget target)
    {
        SelectedTarget = target;
        UpdateCurrentText();
        RefreshSelectedHighlights();
    }

    private void ClearSelectedTarget()
    {
        SelectedTarget = null;
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
    }

    private void UpdateCurrentText()
    {
        _currentTargetText.Text = SelectedTarget is null
            ? "当前映射：未选择"
            : $"当前映射：{SelectedTarget.DisplayName}";
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => CloseWithResult(false);

    private void OnDoneClick(object sender, RoutedEventArgs e) => CloseWithResult(true);

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
}
