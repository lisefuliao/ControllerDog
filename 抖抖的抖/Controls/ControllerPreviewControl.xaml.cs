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
using DouDouDeDou.Services;
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
            new PropertyMetadata("minimal", OnVisualStateChanged));

    public static readonly DependencyProperty IsCalibrationModeProperty =
        DependencyProperty.Register(
            nameof(IsCalibrationMode),
            typeof(bool),
            typeof(ControllerPreviewControl),
            new PropertyMetadata(false, OnVisualStateChanged));

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ControllerLayoutService _layoutService = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly List<HotspotInstance> _hotspots = new();
    private readonly Dictionary<string, AlphaMask> _alphaMasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapSource> _bitmapCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<FrameworkElement, VisualState> _lastVisualStates = new();
    private readonly Dictionary<string, Point> _offsets = new(StringComparer.OrdinalIgnoreCase);
    private INotifyCollectionChanged? _observedPressedCollection;
    private HashSet<string> _pendingPressedButtons = new(StringComparer.OrdinalIgnoreCase);
    private ControllerTypeModel _pendingControllerType = ControllerTypeModel.None;
    private ControllerTypeModel _renderedControllerType = ControllerTypeModel.None;
    private bool _renderedCalibrationMode;
    private string? _renderedControllerAsset;
    private bool _hasRenderedStructure;
    private Window? _hostWindow;
    private HotspotInstance? _dragging;
    private HotspotInstance? _selectedCalibrationHotspot;
    private HotspotInstance? _hoveredHotspot;
    private HotspotInstance? _pressedAlphaHotspot;
    private Point _lastAlphaHitPoint = new(double.NaN, double.NaN);
    private HotspotInstance? _lastAlphaHit;
    private Point _dragStartMouse;
    private Point _dragStartOffset;
    private bool _refreshRequested = true;

    public ControllerPreviewControl()
    {
        InitializeComponent();
        LoadCalibrationOffsets();

        PreviewCanvas.MouseMove += OnPreviewCanvasMouseMove;
        PreviewCanvas.MouseLeave += OnPreviewCanvasMouseLeave;
        PreviewCanvas.PreviewMouseLeftButtonDown += OnPreviewCanvasMouseDown;
        PreviewCanvas.PreviewMouseLeftButtonUp += OnPreviewCanvasMouseUp;
        PreviewCanvas.KeyDown += OnPreviewCanvasKeyDown;

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _refreshTimer.Tick += (_, _) => FlushPendingRefresh();
        _refreshTimer.Start();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
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
        if (IsHostMinimized())
        {
            _refreshRequested = true;
            return;
        }

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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hostWindow = Window.GetWindow(this);
        if (_hostWindow is not null)
        {
            _hostWindow.StateChanged += OnHostWindowStateChanged;
        }

        UpdateRefreshTimerForWindowState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_hostWindow is not null)
        {
            _hostWindow.StateChanged -= OnHostWindowStateChanged;
            _hostWindow = null;
        }

        _refreshTimer.Stop();
    }

    private void OnHostWindowStateChanged(object? sender, EventArgs e)
    {
        UpdateRefreshTimerForWindowState();
    }

    private void UpdateRefreshTimerForWindowState()
    {
        if (IsHostMinimized())
        {
            _refreshTimer.Stop();
            return;
        }

        if (!_refreshTimer.IsEnabled)
        {
            _refreshTimer.Start();
        }

        RequestRefresh();
    }

    private bool IsHostMinimized()
    {
        return _hostWindow?.WindowState == WindowState.Minimized;
    }

    private void FlushPendingRefresh()
    {
        if (!_refreshRequested)
        {
            return;
        }

        _refreshRequested = false;
        var controllerAsset = GetControllerAsset();
        var structureChanged = !_hasRenderedStructure
                               || _renderedControllerType != _pendingControllerType
                               || _renderedCalibrationMode != IsCalibrationMode
                               || !string.Equals(_renderedControllerAsset, controllerAsset, StringComparison.OrdinalIgnoreCase);

        if (structureChanged)
        {
            UpdateControllerImage(controllerAsset);
            RebuildHotspots();
            _renderedControllerType = _pendingControllerType;
            _renderedCalibrationMode = IsCalibrationMode;
            _renderedControllerAsset = controllerAsset;
            _hasRenderedStructure = true;
        }

        CalibrationTools.Visibility = Visibility.Collapsed;
        UpdateHighlights();
    }

    private void UpdateControllerImage(string asset)
    {
        ControllerImage.Source = LoadFrozenBitmap(asset);
        ControllerImage.Opacity = _pendingControllerType == ControllerTypeModel.None ? 0.35 : 1.0;
    }

    private string GetControllerAsset()
    {
        var suffix = IsDarkPalette() ? "dark" : "light";
        return _pendingControllerType switch
        {
            ControllerTypeModel.DualShock4 => $"/Assets/Controllers/DS4/base_{suffix}.png",
            ControllerTypeModel.DualSenseDse => $"/Assets/Controllers/DSE/Base_{ToTitle(suffix)}.png",
            ControllerTypeModel.XInput => $"/Assets/Controllers/Xbox/Base_{ToTitle(suffix)}.png",
            _ => $"/Assets/Controllers/Xbox/Base_{ToTitle(suffix)}.png"
        };
    }

    private static string ToTitle(string value) => string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";

    private void RebuildHotspots()
    {
        PreviewCanvas.Children.Clear();
        PreviewCanvas.Children.Add(ControllerImage);
        _hotspots.Clear();
        _lastVisualStates.Clear();
        _hoveredHotspot = null;
        _pressedAlphaHotspot = null;

        if (IsAlphaOverlayMode())
        {
            RebuildAlphaHotspots();
            return;
        }

        foreach (var definition in GetDefinitions(_pendingControllerType))
        {
            var element = CreateHotspotElement(definition);
            var overlay = CreateButtonOverlay(definition);
            var instance = new HotspotInstance(definition, element, overlay);
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
            if (overlay is not null)
            {
                Panel.SetZIndex(overlay, definition.ZIndex);
                PreviewCanvas.Children.Add(overlay);
            }

            Panel.SetZIndex(element, definition.ZIndex + 1);
            PreviewCanvas.Children.Add(element);

            if (IsCalibrationMode)
            {
                AddCalibrationLabel(instance);
            }
        }
    }

    private void RebuildAlphaHotspots()
    {
        IEnumerable<HotspotDefinition> definitions = _pendingControllerType == ControllerTypeModel.DualShock4
            ? Ds4HitTestPriority
            : GetDefinitions(_pendingControllerType).OrderByDescending(x => x.ZIndex);

        foreach (var definition in definitions)
        {
            var overlay = CreateButtonOverlay(definition);
            if (overlay is null)
            {
                continue;
            }

            _ = GetAlphaMask(definition);
            var element = new Border { IsHitTestVisible = false };
            var instance = new HotspotInstance(definition, element, overlay);
            element.Tag = instance;
            _hotspots.Add(instance);
            PreviewCanvas.Children.Add(overlay);
        }

        PreviewCanvas.Cursor = Cursors.Hand;
        _lastAlphaHitPoint = new Point(double.NaN, double.NaN);
        _lastAlphaHit = null;
    }

    private bool IsAlphaOverlayMode()
    {
        return !IsCalibrationMode && HasAlphaOverlays(_pendingControllerType);
    }

    private bool HasAlphaOverlays(ControllerTypeModel controllerType)
    {
        return controllerType == ControllerTypeModel.DualShock4
               || GetDefinitions(controllerType).Any(definition => GetButtonOverlayAsset(definition) is not null);
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

    private Image? CreateButtonOverlay(HotspotDefinition definition)
    {
        var asset = GetButtonOverlayAsset(definition);
        if (asset is null)
        {
            return null;
        }

        var image = new Image
        {
            Source = LoadFrozenBitmap(asset),
            Width = CanvasWidth,
            Height = CanvasHeight,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
            Opacity = 0
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    private BitmapSource LoadFrozenBitmap(string asset)
    {
        if (_bitmapCache.TryGetValue(asset, out var cached))
        {
            return cached;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = CreateAssetUri(asset);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        bitmap.EndInit();
        bitmap.Freeze();
        _bitmapCache[asset] = bitmap;
        return bitmap;
    }

    private static Uri CreateAssetUri(string asset)
    {
        if (System.IO.Path.IsPathRooted(asset) && File.Exists(asset))
        {
            return new Uri(asset, UriKind.Absolute);
        }

        var normalized = asset.StartsWith("/", StringComparison.Ordinal)
            ? asset
            : "/" + asset;
        return new Uri($"pack://application:,,,{normalized}", UriKind.Absolute);
    }

    private string? GetButtonOverlayAsset(HotspotDefinition definition)
    {
        if (_pendingControllerType != ControllerTypeModel.DualShock4)
        {
            return string.IsNullOrWhiteSpace(definition.HighlightImage) ? null : definition.HighlightImage;
        }

        return $"/Assets/Controllers/DS4/buttons/{definition.Name}.png";
    }

    private AlphaMask? GetAlphaMask(HotspotDefinition definition)
    {
        var asset = GetButtonOverlayAsset(definition);
        if (asset is null)
        {
            return null;
        }

        if (_alphaMasks.TryGetValue(asset, out var cached))
        {
            return cached;
        }

        try
        {
            var source = LoadFrozenBitmap(asset);
            BitmapSource bitmap = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var stride = bitmap.PixelWidth * 4;
            var pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels(pixels, stride, 0);
            var alpha = new byte[bitmap.PixelWidth * bitmap.PixelHeight];
            var minX = bitmap.PixelWidth;
            var minY = bitmap.PixelHeight;
            var maxX = 0;
            var maxY = 0;
            for (var y = 0; y < bitmap.PixelHeight; y++)
            {
                var row = y * stride;
                var alphaRow = y * bitmap.PixelWidth;
                for (var x = 0; x < bitmap.PixelWidth; x++)
                {
                    var value = pixels[row + x * 4 + 3];
                    alpha[alphaRow + x] = value;
                    if (value <= 24)
                    {
                        continue;
                    }

                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }

            if (minX > maxX || minY > maxY)
            {
                minX = minY = maxX = maxY = 0;
            }

            cached = new AlphaMask(bitmap.PixelWidth, bitmap.PixelHeight, alpha, minX, minY, maxX, maxY);
            _alphaMasks[asset] = cached;
            return cached;
        }
        catch
        {
            return null;
        }
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
            _hoveredHotspot = instance;
            ApplyElementVisual(element, VisualState.Hover);
        }
    }

    private void OnPreviewCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (!IsAlphaOverlayMode())
        {
            return;
        }

        var hit = HitTestAlphaOverlay(e.GetPosition(PreviewCanvas));
        if (ReferenceEquals(hit, _hoveredHotspot))
        {
            return;
        }

        _hoveredHotspot = hit;
        UpdateHighlights();
    }

    private void OnPreviewCanvasMouseLeave(object sender, MouseEventArgs e)
    {
        if (!IsAlphaOverlayMode())
        {
            return;
        }

        _hoveredHotspot = null;
        _pressedAlphaHotspot = null;
        UpdateHighlights();
    }

    private void OnPreviewCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsAlphaOverlayMode())
        {
            return;
        }

        _pressedAlphaHotspot = HitTestAlphaOverlay(e.GetPosition(PreviewCanvas));
        if (_pressedAlphaHotspot is not null)
        {
            e.Handled = true;
        }
    }

    private void OnPreviewCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsAlphaOverlayMode())
        {
            return;
        }

        var hit = HitTestAlphaOverlay(e.GetPosition(PreviewCanvas));
        if (hit is not null && ReferenceEquals(hit, _pressedAlphaHotspot) && ButtonCommand?.CanExecute(hit.Definition.SourceButton) == true)
        {
            ButtonCommand.Execute(hit.Definition.SourceButton);
            e.Handled = true;
        }

        _pressedAlphaHotspot = null;
    }

    private HotspotInstance? HitTestAlphaOverlay(Point canvasPoint)
    {
        if (canvasPoint.X < 0 || canvasPoint.Y < 0 || canvasPoint.X >= CanvasWidth || canvasPoint.Y >= CanvasHeight)
        {
            return null;
        }

        if (Math.Abs(canvasPoint.X - _lastAlphaHitPoint.X) < 0.25
            && Math.Abs(canvasPoint.Y - _lastAlphaHitPoint.Y) < 0.25)
        {
            return _lastAlphaHit;
        }

        _lastAlphaHitPoint = canvasPoint;
        _lastAlphaHit = null;
        foreach (var hotspot in _hotspots)
        {
            var mask = GetAlphaMask(hotspot.Definition);
            if (mask is null)
            {
                continue;
            }

            var sourceX = canvasPoint.X / CanvasWidth * mask.Width;
            var sourceY = canvasPoint.Y / CanvasHeight * mask.Height;
            if (sourceX < mask.MinX || sourceX > mask.MaxX || sourceY < mask.MinY || sourceY > mask.MaxY)
            {
                continue;
            }

            if (IsDirectionalDPad(hotspot.Definition) && !IsInsideDefinitionBounds(hotspot.Definition, sourceX, sourceY))
            {
                continue;
            }

            var x = (int)Math.Round(canvasPoint.X / CanvasWidth * (mask.Width - 1));
            var y = (int)Math.Round(canvasPoint.Y / CanvasHeight * (mask.Height - 1));
            if (mask.GetAlpha(x, y) > 24)
            {
                _lastAlphaHit = hotspot;
                return hotspot;
            }
        }

        return null;
    }

    private static bool IsDirectionalDPad(HotspotDefinition definition)
    {
        return definition.Name.StartsWith("DPad", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInsideDefinitionBounds(HotspotDefinition definition, double sourceX, double sourceY)
    {
        const double margin = 8;
        return sourceX >= definition.X - margin
               && sourceX <= definition.X + definition.Width + margin
               && sourceY >= definition.Y - margin
               && sourceY <= definition.Y + definition.Height + margin;
    }

    private void OnHotspotMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is HotspotInstance instance)
        {
            if (ReferenceEquals(_hoveredHotspot, instance))
            {
                _hoveredHotspot = null;
            }

            var active = IsHotspotActive(instance.Definition.SourceButton);
            ApplyElementVisual(element, active ? VisualState.Active : VisualState.Default);
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
            _selectedCalibrationHotspot = instance;
            PreviewCanvas.Focus();
            _dragging = instance;
            _dragStartMouse = e.GetPosition(PreviewCanvas);
            _dragStartOffset = GetOffset(instance.Definition);
            element.CaptureMouse();
            e.Handled = true;
            return;
        }

        ApplyElementVisual(element, VisualState.Active);
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
            UpdateLayoutFromElement(instance);
            _dragging = null;
            SaveLayoutIfJsonBacked(instance.Definition);
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
        ResetJsonBackedButton(instance.Definition);
        RequestRefresh();
        e.Handled = true;
    }

    private void OnPreviewCanvasKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsCalibrationMode || _selectedCalibrationHotspot is null)
        {
            return;
        }

        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        var resize = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var definition = _selectedCalibrationHotspot.Definition;
        var handled = true;

        if (resize)
        {
            switch (e.Key)
            {
                case Key.Left:
                    definition.Width = Math.Max(10, definition.Width - step / ScaleX(1));
                    break;
                case Key.Right:
                    definition.Width += step / ScaleX(1);
                    break;
                case Key.Up:
                    definition.Height = Math.Max(10, definition.Height - step / ScaleY(1));
                    break;
                case Key.Down:
                    definition.Height += step / ScaleY(1);
                    break;
                default:
                    handled = false;
                    break;
            }
        }
        else
        {
            var key = GetOffsetKey(definition);
            var offset = GetOffset(definition);
            _offsets[key] = e.Key switch
            {
                Key.Left => new Point(offset.X - step, offset.Y),
                Key.Right => new Point(offset.X + step, offset.Y),
                Key.Up => new Point(offset.X, offset.Y - step),
                Key.Down => new Point(offset.X, offset.Y + step),
                _ => offset
            };
            handled = e.Key is Key.Left or Key.Right or Key.Up or Key.Down;
        }

        if (!handled)
        {
            return;
        }

        UpdateLayoutFromElement(_selectedCalibrationHotspot);
        SaveLayoutIfJsonBacked(definition);
        RequestRefresh();
        e.Handled = true;
    }

    private void UpdateHighlights()
    {
        var disconnected = _pendingControllerType == ControllerTypeModel.None;
        foreach (var hotspot in _hotspots)
        {
            var active = IsHotspotActive(hotspot.Definition.SourceButton);
            var state = disconnected
                ? VisualState.Disabled
                : active
                    ? VisualState.Active
                    : ReferenceEquals(_hoveredHotspot, hotspot)
                        ? VisualState.Hover
                        : VisualState.Default;
            ApplyElementVisual(hotspot.Element, state);
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
        if (_lastVisualStates.TryGetValue(element, out var previous) && previous == state)
        {
            return;
        }

        _lastVisualStates[element] = state;

        var accent = GetAccentBrush();
        var hasBitmapOverlay = element.Tag is HotspotInstance instance && instance.Overlay is not null;
        if (element.Tag is HotspotInstance overlayInstance && overlayInstance.Overlay is not null)
        {
            overlayInstance.Overlay.Opacity = state switch
            {
                VisualState.Active => 1.0,
                VisualState.Hover => 0.78,
                _ => 0
            };
        }

        var fill = state switch
        {
            VisualState.Active => hasBitmapOverlay ? Brushes.Transparent : GetActiveOverlayBrush(),
            VisualState.Hover => hasBitmapOverlay ? Brushes.Transparent : GetHoverBrush(),
            VisualState.Disabled => IsCalibrationMode ? GetDisabledBrush() : Brushes.Transparent,
            _ => Brushes.Transparent
        };
        var stroke = state switch
        {
            VisualState.Active or VisualState.Hover => hasBitmapOverlay && !IsCalibrationMode ? Brushes.Transparent : accent,
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

    private IReadOnlyList<HotspotDefinition> GetDefinitions(ControllerTypeModel controllerType)
    {
        return controllerType switch
        {
            ControllerTypeModel.DualShock4 => Ds4Definitions,
            ControllerTypeModel.DualSenseDse or ControllerTypeModel.XInput => LoadJsonDefinitions(controllerType),
            _ => LoadJsonDefinitions(ControllerTypeModel.XInput)
        };
    }

    private IReadOnlyList<HotspotDefinition> LoadJsonDefinitions(ControllerTypeModel controllerType)
    {
        var layout = _layoutService.LoadLayout(controllerType);
        if (layout.ButtonItems.Count == 0)
        {
            return controllerType == ControllerTypeModel.DualSenseDse ? DualSenseDefinitions : XInputDefinitions;
        }

        return layout.ButtonItems
            .OrderBy(item => item.ZIndex)
            .Select(item => new HotspotDefinition(
                item.ButtonId,
                item.ButtonId,
                string.IsNullOrWhiteSpace(item.Label) ? item.ButtonId : item.Label,
                item.IsRectangle ? HotspotShape.Rectangle : HotspotShape.Circle,
                item.X,
                item.Y,
                item.Width,
                item.Height,
                item.IsRectangle ? Math.Min(item.Width, item.Height) * 0.22 : Math.Min(item.Width, item.Height) / 2)
            {
                HighlightImage = item.HighlightImage,
                Rotation = item.Rotation,
                IsTemporary = item.IsTemporary,
                ZIndex = item.ZIndex
            })
            .ToList();
    }

    private void UpdateLayoutFromElement(HotspotInstance instance)
    {
        if (_pendingControllerType == ControllerTypeModel.DualShock4)
        {
            return;
        }

        var definition = instance.Definition;
        var offset = GetOffset(definition);
        definition.X += offset.X / CanvasWidth * SourceWidth;
        definition.Y += offset.Y / CanvasHeight * SourceHeight;
        _offsets.Remove(GetOffsetKey(definition));
        ApplyPosition(instance);
    }

    private void SaveLayoutIfJsonBacked(HotspotDefinition definition)
    {
        if (_pendingControllerType == ControllerTypeModel.DualShock4)
        {
            SaveCalibrationOffsets();
            return;
        }

        var layout = _layoutService.LoadLayout(_pendingControllerType);
        var item = layout.ButtonItems.FirstOrDefault(x => string.Equals(x.ButtonId, definition.Name, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return;
        }

        item.X = definition.X;
        item.Y = definition.Y;
        item.Width = definition.Width;
        item.Height = definition.Height;
        item.Rotation = definition.Rotation;
        _layoutService.SaveLayout(_pendingControllerType, layout);
    }

    private void ResetJsonBackedButton(HotspotDefinition definition)
    {
        if (_pendingControllerType == ControllerTypeModel.DualShock4)
        {
            SaveCalibrationOffsets();
            return;
        }

        var current = _layoutService.LoadLayout(_pendingControllerType);
        _layoutService.ResetUserLayout(_pendingControllerType);
        var defaults = _layoutService.LoadLayout(_pendingControllerType);
        var source = defaults.ButtonItems.FirstOrDefault(x => string.Equals(x.ButtonId, definition.Name, StringComparison.OrdinalIgnoreCase));
        var target = current.ButtonItems.FirstOrDefault(x => string.Equals(x.ButtonId, definition.Name, StringComparison.OrdinalIgnoreCase));
        if (source is null || target is null)
        {
            return;
        }

        target.X = source.X;
        target.Y = source.Y;
        target.Width = source.Width;
        target.Height = source.Height;
        target.Rotation = source.Rotation;
        _layoutService.SaveLayout(_pendingControllerType, current);
    }

    private void OnSaveCurrentOverlayClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCalibrationHotspot is null)
        {
            return;
        }

        SaveLayoutIfJsonBacked(_selectedCalibrationHotspot.Definition);
    }

    private void OnExportCurrentOverlayClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCalibrationHotspot is null)
        {
            return;
        }

        ExportOverlay(_selectedCalibrationHotspot.Definition);
        RequestRefresh();
    }

    private void OnExportAllOverlaysClick(object sender, RoutedEventArgs e)
    {
        if (_pendingControllerType == ControllerTypeModel.DualShock4)
        {
            return;
        }

        foreach (var hotspot in _hotspots)
        {
            ExportOverlay(hotspot.Definition);
        }

        RequestRefresh();
    }

    private void OnResetCurrentOverlayClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCalibrationHotspot is null)
        {
            return;
        }

        ResetJsonBackedButton(_selectedCalibrationHotspot.Definition);
        RequestRefresh();
    }

    private void ExportOverlay(HotspotDefinition definition)
    {
        if (_pendingControllerType == ControllerTypeModel.DualShock4)
        {
            return;
        }

        var baseAsset = GetControllerAsset();
        var source = LoadFrozenBitmap(baseAsset);
        BitmapSource bitmap = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        var output = new byte[pixels.Length];
        bitmap.CopyPixels(pixels, stride, 0);

        var x0 = Math.Clamp((int)Math.Floor(definition.X), 0, bitmap.PixelWidth - 1);
        var y0 = Math.Clamp((int)Math.Floor(definition.Y), 0, bitmap.PixelHeight - 1);
        var x1 = Math.Clamp((int)Math.Ceiling(definition.X + definition.Width), 0, bitmap.PixelWidth);
        var y1 = Math.Clamp((int)Math.Ceiling(definition.Y + definition.Height), 0, bitmap.PixelHeight);
        var accent = (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Color.FromRgb(217, 87, 130);

        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var mask = GetMaskAlpha(definition, x + 0.5, y + 0.5);
                if (mask <= 0)
                {
                    continue;
                }

                var index = y * stride + x * 4;
                var sourceAlpha = pixels[index + 3];
                if (sourceAlpha <= 0)
                {
                    continue;
                }

                output[index] = (byte)Math.Clamp(pixels[index] * 0.58 + accent.B * 0.42, 0, 255);
                output[index + 1] = (byte)Math.Clamp(pixels[index + 1] * 0.58 + accent.G * 0.42, 0, 255);
                output[index + 2] = (byte)Math.Clamp(pixels[index + 2] * 0.58 + accent.R * 0.42, 0, 255);
                output[index + 3] = (byte)Math.Clamp(sourceAlpha * mask * 0.78, 0, 255);
            }
        }

        var overlay = BitmapSource.Create(bitmap.PixelWidth, bitmap.PixelHeight, bitmap.DpiX, bitmap.DpiY, PixelFormats.Bgra32, null, output, stride);
        var path = GetOverlayOutputPath(_pendingControllerType, definition);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using (var stream = File.Create(path))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(overlay));
            encoder.Save(stream);
        }

        if (!PngHasClearAlpha(path))
        {
            return;
        }

        var layout = _layoutService.LoadLayout(_pendingControllerType);
        var item = layout.ButtonItems.FirstOrDefault(x => string.Equals(x.ButtonId, definition.Name, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return;
        }

        item.HighlightImage = path;
        item.X = definition.X;
        item.Y = definition.Y;
        item.Width = definition.Width;
        item.Height = definition.Height;
        item.Rotation = definition.Rotation;
        item.IsTemporary = false;
        _layoutService.SaveLayout(_pendingControllerType, layout);
        definition.HighlightImage = path;
        definition.IsTemporary = false;
    }

    private static double GetMaskAlpha(HotspotDefinition definition, double x, double y)
    {
        var localX = (x - definition.X) / Math.Max(1, definition.Width);
        var localY = (y - definition.Y) / Math.Max(1, definition.Height);
        if (definition.Shape == HotspotShape.Circle)
        {
            var dx = (localX - 0.5) * 2;
            var dy = (localY - 0.5) * 2;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            return distance <= 0.96 ? 1 : distance >= 1 ? 0 : (1 - distance) / 0.04;
        }

        return localX is >= 0 and <= 1 && localY is >= 0 and <= 1 ? 1 : 0;
    }

    private static string GetOverlayOutputPath(ControllerTypeModel controllerType, HotspotDefinition definition)
    {
        var projectDir = FindProjectDirectory();
        var controllerFolder = controllerType == ControllerTypeModel.DualSenseDse ? "DSE" : "Xbox";
        var fileName = definition.Name switch
        {
            "A" when controllerType == ControllerTypeModel.DualSenseDse => "Cross",
            "B" when controllerType == ControllerTypeModel.DualSenseDse => "Circle",
            "X" when controllerType == ControllerTypeModel.DualSenseDse => "Square",
            "Y" when controllerType == ControllerTypeModel.DualSenseDse => "Triangle",
            _ => definition.Name
        };
        return System.IO.Path.Combine(projectDir, "Assets", "Controllers", controllerFolder, "Buttons", $"{fileName}.png");
    }

    private static string FindProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "抖抖的抖.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }

    private static bool PngHasClearAlpha(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            BitmapSource source = bitmap.Format == PixelFormats.Bgra32
                ? bitmap
                : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var stride = source.PixelWidth * 4;
            var pixels = new byte[stride * source.PixelHeight];
            source.CopyPixels(pixels, stride, 0);
            var minAlpha = 255;
            var maxAlpha = 0;
            for (var i = 3; i < pixels.Length; i += 4)
            {
                minAlpha = Math.Min(minAlpha, pixels[i]);
                maxAlpha = Math.Max(maxAlpha, pixels[i]);
            }

            return minAlpha < 10 && maxAlpha > 10;
        }
        catch
        {
            return false;
        }
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

    private static readonly IReadOnlyList<HotspotDefinition> Ds4HitTestPriority =
    [
        Circle("Triangle", "Triangle", "Triangle", 1162, 208, 86),
        Circle("Square", "Square", "Square", 1074, 306, 86),
        Circle("Circle", "Circle", "Circle", 1260, 306, 86),
        Circle("Cross", "Cross", "Cross", 1172, 405, 86),
        Rect("Share", "Share", "Share", 507, 168, 41, 78, 20),
        Rect("Options", "Options", "Options", 1038, 168, 41, 78, 20),
        Circle("PS", "PS", "PS", 755, 494, 74),
        Rect("DPadUp", "DPadUp", "DPadUp", 341, 236, 76, 90, 22),
        Rect("DPadLeft", "DPadLeft", "DPadLeft", 274, 306, 92, 78, 22),
        Rect("DPadRight", "DPadRight", "DPadRight", 413, 306, 92, 78, 22),
        Rect("DPadDown", "DPadDown", "DPadDown", 341, 381, 76, 90, 22),
        Rect("L2", "L2", "L2", 329, 52, 136, 45, 20),
        Rect("L1", "L1", "L1", 319, 96, 154, 40, 18),
        Rect("R2", "R2", "R2", 1121, 52, 136, 45, 20),
        Rect("R1", "R1", "R1", 1112, 96, 154, 40, 18),
        Circle("LeftStick", "LeftStick", "LeftStick", 499, 449, 158),
        Circle("RightStick", "RightStick", "RightStick", 916, 449, 158),
        Rect("Touchpad", "Touchpad", "Touchpad", 579, 153, 426, 226, 24)
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

    private sealed class HotspotDefinition
    {
        public HotspotDefinition(string name, string sourceButton, string displayName, HotspotShape shape, double x, double y, double width, double height, double cornerRadius)
        {
            Name = name;
            SourceButton = sourceButton;
            DisplayName = displayName;
            Shape = shape;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            CornerRadius = cornerRadius;
        }

        public string Name { get; }
        public string SourceButton { get; }
        public string DisplayName { get; }
        public HotspotShape Shape { get; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double CornerRadius { get; }
        public string? HighlightImage { get; set; }
        public double Rotation { get; set; }
        public bool IsTemporary { get; set; }
        public int ZIndex { get; set; } = 10;
    }

    private sealed record HotspotInstance(HotspotDefinition Definition, FrameworkElement Element, Image? Overlay);

    private sealed record SavedOffset(double X, double Y);

    private sealed record AlphaMask(int Width, int Height, byte[] Alpha, int MinX, int MinY, int MaxX, int MaxY)
    {
        public byte GetAlpha(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
            {
                return 0;
            }

            return Alpha[y * Width + x];
        }
    }

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
