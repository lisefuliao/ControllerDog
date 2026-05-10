using System.Collections;
using System.Collections.Specialized;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DouDouDeDou.Models;
using ControllerTypeModel = DouDouDeDou.Models.ControllerType;

namespace DouDouDeDou.Controls;

public partial class ControllerPreviewControl : UserControl
{
    private const double CanvasWidth = 360;
    private const double CanvasHeight = 230;
    private const double SourceWidth = 1586;
    private const double SourceHeight = 992;

    public static readonly DependencyProperty ControllerTypeProperty =
        DependencyProperty.Register(
            nameof(ControllerType),
            typeof(ControllerTypeModel),
            typeof(ControllerPreviewControl),
            new PropertyMetadata(ControllerTypeModel.None, OnVisualStateChanged));

    public static readonly DependencyProperty PressedButtonsProperty =
        DependencyProperty.Register(
            nameof(PressedButtons),
            typeof(IEnumerable),
            typeof(ControllerPreviewControl),
            new PropertyMetadata(null, OnPressedButtonsChanged));

    public static readonly DependencyProperty ButtonCommandProperty =
        DependencyProperty.Register(
            nameof(ButtonCommand),
            typeof(ICommand),
            typeof(ControllerPreviewControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty AppearanceStyleProperty =
        DependencyProperty.Register(
            nameof(AppearanceStyle),
            typeof(string),
            typeof(ControllerPreviewControl),
            new PropertyMetadata("minimal"));

    public static readonly DependencyProperty IsCalibrationModeProperty =
        DependencyProperty.Register(
            nameof(IsCalibrationMode),
            typeof(bool),
            typeof(ControllerPreviewControl),
            new PropertyMetadata(false, OnVisualStateChanged));

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly DispatcherTimer _refreshTimer;
    private readonly List<HotspotInstance> _hotspots = new();
    private readonly Dictionary<string, Point> _offsets = new(StringComparer.OrdinalIgnoreCase);
    private INotifyCollectionChanged? _observedPressedCollection;
    private HashSet<string> _pendingPressedButtons = new(StringComparer.OrdinalIgnoreCase);
    private ControllerTypeModel _pendingControllerType = ControllerTypeModel.None;
    private HotspotInstance? _dragging;
    private Point _dragStartMouse;
    private Point _dragStartOffset;
    private bool _refreshRequested = true;

    public ControllerPreviewControl()
    {
        InitializeComponent();
        LoadCalibrationOffsets();

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _refreshTimer.Tick += (_, _) => FlushPendingRefresh();
        _refreshTimer.Start();
        Unloaded += (_, _) => _refreshTimer.Stop();
        RequestRefresh();
    }

    public ControllerTypeModel ControllerType
    {
        get => (ControllerTypeModel)GetValue(ControllerTypeProperty);
        set => SetValue(ControllerTypeProperty, value);
    }

    public IEnumerable? PressedButtons
    {
        get => (IEnumerable?)GetValue(PressedButtonsProperty);
        set => SetValue(PressedButtonsProperty, value);
    }

    public ICommand? ButtonCommand
    {
        get => (ICommand?)GetValue(ButtonCommandProperty);
        set => SetValue(ButtonCommandProperty, value);
    }

    public string AppearanceStyle
    {
        get => (string)GetValue(AppearanceStyleProperty);
        set => SetValue(AppearanceStyleProperty, value);
    }

    public bool IsCalibrationMode
    {
        get => (bool)GetValue(IsCalibrationModeProperty);
        set => SetValue(IsCalibrationModeProperty, value);
    }

    private static void OnVisualStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ControllerPreviewControl)d).RequestRefresh();
    }

    private static void OnPressedButtonsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ControllerPreviewControl)d;
        if (control._observedPressedCollection is not null)
        {
            control._observedPressedCollection.CollectionChanged -= control.OnPressedCollectionChanged;
        }

        control._observedPressedCollection = e.NewValue as INotifyCollectionChanged;
        if (control._observedPressedCollection is not null)
        {
            control._observedPressedCollection.CollectionChanged += control.OnPressedCollectionChanged;
        }

        control.RequestRefresh();
    }

    private void OnPressedCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RequestRefresh();
    }

    private void RequestRefresh()
    {
        _pendingPressedButtons = GetPressedCanonicalButtons();
        _pendingControllerType = ControllerType;
        _refreshRequested = true;
        if (Dispatcher.CheckAccess())
        {
            FlushPendingRefresh();
            return;
        }

        Dispatcher.BeginInvoke(FlushPendingRefresh, DispatcherPriority.Render);
    }

    private void FlushPendingRefresh()
    {
        if (!_refreshRequested)
        {
            return;
        }

        _refreshRequested = false;
        UpdateControllerImage();
        RebuildHotspots();
        UpdateHighlights();
    }

    private void UpdateControllerImage()
    {
        var asset = GetControllerAsset();
        ControllerImage.Source = new BitmapImage(new Uri(asset, UriKind.Relative));
        ControllerImage.Opacity = _pendingControllerType == ControllerTypeModel.None ? 0.35 : 1.0;
    }

    private string GetControllerAsset()
    {
        var suffix = IsDarkPalette() ? "dark" : "light";
        return _pendingControllerType switch
        {
            ControllerTypeModel.DualShock4 => $"/Assets/Images/ds4_{suffix}.png",
            ControllerTypeModel.DualSenseDse => $"/Assets/Images/dualsense_{suffix}.png",
            ControllerTypeModel.XInput => $"/Assets/Images/xinput_{suffix}.png",
            _ => $"/Assets/Images/xinput_{suffix}.png"
        };
    }

    private void RebuildHotspots()
    {
        PreviewCanvas.Children.Clear();
        PreviewCanvas.Children.Add(ControllerImage);
        _hotspots.Clear();

        foreach (var definition in GetDefinitions(_pendingControllerType))
        {
            var element = CreateHotspotElement(definition);
            var instance = new HotspotInstance(definition, element);
            _hotspots.Add(instance);

            element.Tag = instance;
            element.Cursor = IsCalibrationMode ? Cursors.SizeAll : Cursors.Hand;
            element.ToolTip = IsCalibrationMode
                ? $"{definition.DisplayName}：拖动校准位置，右键恢复默认"
                : $"点击设置 {definition.DisplayName} 映射";
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = new ScaleTransform(1, 1);
            element.MouseEnter += OnHotspotMouseEnter;
            element.MouseLeave += OnHotspotMouseLeave;
            element.PreviewMouseLeftButtonDown += OnHotspotMouseDown;
            element.MouseMove += OnHotspotMouseMove;
            element.PreviewMouseLeftButtonUp += OnHotspotMouseUp;
            element.MouseRightButtonUp += OnHotspotRightClick;

            ApplyPosition(instance);
            PreviewCanvas.Children.Add(element);

            if (IsCalibrationMode)
            {
                AddCalibrationLabel(instance);
            }
        }
    }

    private static FrameworkElement CreateHotspotElement(HotspotDefinition definition)
    {
        if (definition.Shape == HotspotShape.Circle)
        {
            return new Ellipse
            {
                Fill = Brushes.Transparent,
                Stroke = Brushes.Transparent,
                StrokeThickness = 1.2,
                Width = ScaleX(definition.Width),
                Height = ScaleY(definition.Height),
                SnapsToDevicePixels = true
            };
        }

        return new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(ScaleX(definition.CornerRadius)),
            Width = ScaleX(definition.Width),
            Height = ScaleY(definition.Height),
            SnapsToDevicePixels = true
        };
    }

    private void ApplyPosition(HotspotInstance instance)
    {
        var definition = instance.Definition;
        var offset = GetOffset(definition);
        Canvas.SetLeft(instance.Element, ScaleX(definition.X) + offset.X);
        Canvas.SetTop(instance.Element, ScaleY(definition.Y) + offset.Y);
    }

    private void AddCalibrationLabel(HotspotInstance instance)
    {
        var label = new TextBlock
        {
            Text = instance.Definition.DisplayName,
            FontSize = 7,
            FontWeight = FontWeights.SemiBold,
            Foreground = GetAccentBrush(),
            IsHitTestVisible = false
        };

        Canvas.SetLeft(label, Canvas.GetLeft(instance.Element));
        Canvas.SetTop(label, Canvas.GetTop(instance.Element) - 9);
        PreviewCanvas.Children.Add(label);
    }

    private void OnHotspotMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is HotspotInstance instance && !IsHotspotActive(instance.Definition.SourceButton))
        {
            ApplyElementVisual(element, VisualState.Hover);
            AnimateHotspot(element, 1.025, 120, new CubicEase { EasingMode = EasingMode.EaseOut });
        }
    }

    private void OnHotspotMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is HotspotInstance instance)
        {
            var active = IsHotspotActive(instance.Definition.SourceButton);
            ApplyElementVisual(element, active ? VisualState.Active : VisualState.Default);
            AnimateHotspot(element, 1.0, 120, new CubicEase { EasingMode = EasingMode.EaseOut });
        }
    }

    private void OnHotspotMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not HotspotInstance instance)
        {
            return;
        }

        if (IsCalibrationMode)
        {
            _dragging = instance;
            _dragStartMouse = e.GetPosition(PreviewCanvas);
            _dragStartOffset = GetOffset(instance.Definition);
            element.CaptureMouse();
            e.Handled = true;
            return;
        }

        AnimateHotspot(element, 0.965, 90, new QuadraticEase { EasingMode = EasingMode.EaseOut });
    }

    private void OnHotspotMouseMove(object sender, MouseEventArgs e)
    {
        if (!IsCalibrationMode || _dragging is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(PreviewCanvas);
        var delta = position - _dragStartMouse;
        var key = GetOffsetKey(_dragging.Definition);
        _offsets[key] = new Point(_dragStartOffset.X + delta.X, _dragStartOffset.Y + delta.Y);
        ApplyPosition(_dragging);
        e.Handled = true;
    }

    private void OnHotspotMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not HotspotInstance instance)
        {
            return;
        }

        if (IsCalibrationMode)
        {
            element.ReleaseMouseCapture();
            _dragging = null;
            SaveCalibrationOffsets();
            RequestRefresh();
            e.Handled = true;
            return;
        }

        if (ButtonCommand?.CanExecute(instance.Definition.SourceButton) == true)
        {
            ButtonCommand.Execute(instance.Definition.SourceButton);
            e.Handled = true;
        }
    }

    private void OnHotspotRightClick(object sender, MouseButtonEventArgs e)
    {
        if (!IsCalibrationMode || sender is not FrameworkElement { Tag: HotspotInstance instance })
        {
            return;
        }

        _offsets.Remove(GetOffsetKey(instance.Definition));
        SaveCalibrationOffsets();
        RequestRefresh();
        e.Handled = true;
    }

    private void UpdateHighlights()
    {
        var disconnected = _pendingControllerType == ControllerTypeModel.None;
        foreach (var hotspot in _hotspots)
        {
            var active = IsHotspotActive(hotspot.Definition.SourceButton);
            ApplyElementVisual(hotspot.Element, disconnected ? VisualState.Disabled : active ? VisualState.Active : VisualState.Default);
        }
    }

    private bool IsHotspotActive(string sourceButton)
    {
        return ControllerState.SplitAliases(sourceButton)
            .Any(alias => _pendingPressedButtons.Contains(alias));
    }

    private HashSet<string> GetPressedCanonicalButtons()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (PressedButtons is null)
        {
            return result;
        }

        foreach (var item in PressedButtons)
        {
            if (item is string text)
            {
                result.Add(ControllerState.NormalizeAlias(text));
            }
        }

        return result;
    }

    private void ApplyElementVisual(FrameworkElement element, VisualState state)
    {
        var accent = GetAccentBrush();
        var fill = state switch
        {
            VisualState.Active => GetActiveOverlayBrush(),
            VisualState.Hover => GetHoverBrush(),
            VisualState.Disabled => IsCalibrationMode ? GetDisabledBrush() : Brushes.Transparent,
            _ => Brushes.Transparent
        };
        var stroke = state switch
        {
            VisualState.Active or VisualState.Hover => accent,
            VisualState.Disabled => IsCalibrationMode ? GetDisabledBrush() : Brushes.Transparent,
            _ => IsCalibrationMode ? accent : Brushes.Transparent
        };

        switch (element)
        {
            case Shape shape:
                shape.Fill = fill;
                shape.Stroke = stroke;
                shape.StrokeThickness = state == VisualState.Active ? 2.0 : IsCalibrationMode ? 1.2 : 1.4;
                break;
            case Border border:
                border.Background = fill;
                border.BorderBrush = stroke;
                border.BorderThickness = new Thickness(state == VisualState.Active ? 2.0 : IsCalibrationMode ? 1.2 : 1.4);
                break;
        }
    }

    private Point GetOffset(HotspotDefinition definition)
    {
        return _offsets.TryGetValue(GetOffsetKey(definition), out var offset) ? offset : new Point();
    }

    private string GetOffsetKey(HotspotDefinition definition)
    {
        return $"{GetControllerKey(_pendingControllerType)}:{definition.Name}";
    }

    private static string GetControllerKey(ControllerTypeModel controllerType)
    {
        return controllerType switch
        {
            ControllerTypeModel.DualShock4 => "ds4",
            ControllerTypeModel.DualSenseDse => "dualsense",
            ControllerTypeModel.XInput => "xinput",
            _ => "xinput"
        };
    }

    private void LoadCalibrationOffsets()
    {
        try
        {
            var path = GetCalibrationPath();
            if (!File.Exists(path))
            {
                return;
            }

            var saved = JsonSerializer.Deserialize<Dictionary<string, SavedOffset>>(File.ReadAllText(path));
            if (saved is null)
            {
                return;
            }

            foreach (var (key, value) in saved)
            {
                _offsets[key] = new Point(value.X, value.Y);
            }
        }
        catch
        {
            // Bad calibration data should never stop the mapping tool from opening.
        }
    }

    private void SaveCalibrationOffsets()
    {
        try
        {
            var path = GetCalibrationPath();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var saved = _offsets.ToDictionary(
                item => item.Key,
                item => new SavedOffset(item.Value.X, item.Value.Y),
                StringComparer.OrdinalIgnoreCase);
            File.WriteAllText(path, JsonSerializer.Serialize(saved, JsonOptions));
        }
        catch
        {
            // Calibration is a visual aid; input mapping must keep running even if saving fails.
        }
    }

    private static string GetCalibrationPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return System.IO.Path.Combine(root, "DouDouDeDou", "controller-hotspots.json");
    }

    private static IReadOnlyList<HotspotDefinition> GetDefinitions(ControllerTypeModel controllerType)
    {
        return controllerType switch
        {
            ControllerTypeModel.DualShock4 => Ds4Definitions,
            ControllerTypeModel.DualSenseDse => DualSenseDefinitions,
            ControllerTypeModel.XInput => XInputDefinitions,
            _ => XInputDefinitions
        };
    }

    private static double ScaleX(double value) => value / SourceWidth * CanvasWidth;

    private static double ScaleY(double value) => value / SourceHeight * CanvasHeight;

    private static Brush GetAccentBrush() =>
        Application.Current.Resources["AccentBrush"] as Brush ?? new SolidColorBrush(Color.FromRgb(37, 99, 235));

    private static Brush GetHoverBrush()
    {
        var color = (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Color.FromRgb(37, 99, 235);
        return new SolidColorBrush(Color.FromArgb(52, color.R, color.G, color.B));
    }

    private static Brush GetActiveOverlayBrush()
    {
        var color = (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Color.FromRgb(37, 99, 235);
        return new SolidColorBrush(Color.FromArgb(150, color.R, color.G, color.B));
    }

    private static bool IsDarkPalette()
    {
        if (Application.Current.Resources["BackgroundBrush"] is not SolidColorBrush brush)
        {
            return false;
        }

        var color = brush.Color;
        var luminance = color.R * 0.2126 + color.G * 0.7152 + color.B * 0.0722;
        return luminance < 80;
    }

    private static Brush GetDisabledBrush() =>
        Application.Current.Resources["DisabledBrush"] as Brush ?? new SolidColorBrush(Color.FromRgb(238, 241, 246));

    private static void AnimateHotspot(FrameworkElement element, double scale, int milliseconds, IEasingFunction easing)
    {
        if (element.RenderTransform is not ScaleTransform transform)
        {
            transform = new ScaleTransform(1, 1);
            element.RenderTransform = transform;
        }

        var duration = TimeSpan.FromMilliseconds(milliseconds);
        var animation = new DoubleAnimation(scale, duration) { EasingFunction = easing };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    private static readonly IReadOnlyList<HotspotDefinition> Ds4Definitions =
    [
        Circle("Triangle", "Triangle", "Triangle", 1162, 208, 86),
        Circle("Square", "Square", "Square", 1074, 306, 86),
        Circle("Circle", "Circle", "Circle", 1260, 306, 86),
        Circle("Cross", "Cross", "Cross", 1172, 405, 86),
        Rect("DPadUp", "DPadUp", "DPadUp", 341, 236, 76, 90, 22),
        Rect("DPadLeft", "DPadLeft", "DPadLeft", 274, 306, 92, 78, 22),
        Rect("DPadRight", "DPadRight", "DPadRight", 413, 306, 92, 78, 22),
        Rect("DPadDown", "DPadDown", "DPadDown", 341, 381, 76, 90, 22),
        Circle("LeftStick", "LeftStick", "LeftStick", 499, 449, 158),
        Circle("RightStick", "RightStick", "RightStick", 916, 449, 158),
        Circle("PS", "PS", "PS", 755, 494, 74),
        Rect("Touchpad", "Touchpad", "Touchpad", 579, 153, 426, 226, 24),
        Rect("Share", "Share", "Share", 507, 168, 41, 78, 20),
        Rect("Options", "Options", "Options", 1038, 168, 41, 78, 20),
        Rect("L2", "L2", "L2", 329, 52, 136, 45, 20),
        Rect("L1", "L1", "L1", 319, 96, 154, 40, 18),
        Rect("R2", "R2", "R2", 1121, 52, 136, 45, 20),
        Rect("R1", "R1", "R1", 1112, 96, 154, 40, 18)
    ];

    private static readonly IReadOnlyList<HotspotDefinition> DualSenseDefinitions =
    [
        Circle("Triangle", "Triangle", "Triangle", 1160, 244, 82),
        Circle("Square", "Square", "Square", 1054, 358, 82),
        Circle("Circle", "Circle", "Circle", 1250, 360, 82),
        Circle("Cross", "Cross", "Cross", 1146, 462, 82),
        Rect("DPadUp", "DPadUp", "DPadUp", 375, 268, 70, 88, 20),
        Rect("DPadLeft", "DPadLeft", "DPadLeft", 303, 341, 86, 72, 20),
        Rect("DPadRight", "DPadRight", "DPadRight", 447, 341, 86, 72, 20),
        Rect("DPadDown", "DPadDown", "DPadDown", 375, 414, 70, 88, 20),
        Circle("LeftStick", "LeftStick", "LeftStick", 521, 486, 150),
        Circle("RightStick", "RightStick", "RightStick", 910, 486, 150),
        Circle("PS", "PS", "PS", 753, 514, 74),
        Rect("Touchpad", "Touchpad", "Touchpad", 540, 118, 507, 305, 38),
        Rect("Create", "Create", "Create", 493, 181, 34, 68, 18),
        Rect("Options", "Options", "Options", 1060, 181, 34, 68, 18),
        Rect("L2", "L2", "L2", 321, 64, 170, 48, 22),
        Rect("L1", "L1", "L1", 321, 109, 174, 64, 26),
        Rect("R2", "R2", "R2", 1095, 64, 170, 48, 22),
        Rect("R1", "R1", "R1", 1092, 109, 174, 64, 26),
        Rect("Mute", "Mute", "Mute", 765, 606, 62, 28, 14)
    ];

    private static readonly IReadOnlyList<HotspotDefinition> XInputDefinitions =
    [
        Circle("Y", "Y", "Y", 1114, 190, 74),
        Circle("X", "X", "X", 1017, 307, 74),
        Circle("B", "B", "B", 1218, 307, 74),
        Circle("A", "A", "A", 1119, 407, 74),
        Circle("LeftStick", "LeftStick", "LeftStick", 351, 245, 146),
        Circle("RightStick", "RightStick", "RightStick", 908, 455, 146),
        Circle("Back", "Back / View", "Back / View", 660, 298, 58),
        Circle("Start", "Start / Menu", "Start / Menu", 867, 298, 58),
        Circle("Xbox", "PS", "Xbox", 740, 141, 104),
        Rect("DPadUp", "DPadUp", "DPadUp", 578, 446, 62, 62, 13),
        Rect("DPadLeft", "DPadLeft", "DPadLeft", 522, 511, 62, 62, 13),
        Rect("DPadRight", "DPadRight", "DPadRight", 641, 511, 62, 62, 13),
        Rect("DPadDown", "DPadDown", "DPadDown", 580, 576, 62, 62, 13),
        Rect("LT", "LT", "LT", 330, 43, 170, 55, 22),
        Rect("LB", "LB", "LB", 303, 105, 260, 55, 22),
        Rect("RT", "RT", "RT", 1085, 43, 170, 55, 22),
        Rect("RB", "RB", "RB", 1025, 105, 260, 55, 22)
    ];

    private static HotspotDefinition Circle(string name, string sourceButton, string displayName, double x, double y, double size)
    {
        return new HotspotDefinition(name, sourceButton, displayName, HotspotShape.Circle, x, y, size, size, size / 2);
    }

    private static HotspotDefinition Rect(string name, string sourceButton, string displayName, double x, double y, double width, double height, double cornerRadius)
    {
        return new HotspotDefinition(name, sourceButton, displayName, HotspotShape.Rectangle, x, y, width, height, cornerRadius);
    }

    private sealed record HotspotDefinition(
        string Name,
        string SourceButton,
        string DisplayName,
        HotspotShape Shape,
        double X,
        double Y,
        double Width,
        double Height,
        double CornerRadius);

    private sealed record HotspotInstance(HotspotDefinition Definition, FrameworkElement Element);

    private sealed record SavedOffset(double X, double Y);

    private enum HotspotShape
    {
        Rectangle,
        Circle
    }

    private enum VisualState
    {
        Default,
        Hover,
        Active,
        Disabled
    }
}
