using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DouDouDeDou.Models;
using ControllerTypeModel = DouDouDeDou.Models.ControllerType;

namespace DouDouDeDou.Controls;

public partial class DevicePreviewControl : UserControl
{
    public static readonly DependencyProperty ControllerTypeProperty =
        DependencyProperty.Register(
            nameof(ControllerType),
            typeof(ControllerTypeModel),
            typeof(DevicePreviewControl),
            new PropertyMetadata(ControllerTypeModel.None, OnVisualStateChanged));

    public static readonly DependencyProperty PressedButtonsProperty =
        DependencyProperty.Register(
            nameof(PressedButtons),
            typeof(IEnumerable),
            typeof(DevicePreviewControl),
            new PropertyMetadata(null, OnPressedButtonsChanged));

    private readonly Brush _idleBrush = new SolidColorBrush(Color.FromRgb(255, 248, 250));
    private readonly Brush _triggerIdleBrush = new SolidColorBrush(Color.FromRgb(255, 241, 245));
    private readonly Brush _activeBrush = new SolidColorBrush(Color.FromRgb(233, 133, 163));
    private readonly Brush _disconnectedBrush = new SolidColorBrush(Color.FromRgb(232, 226, 229));
    private readonly Brush _textBrush = new SolidColorBrush(Color.FromRgb(43, 43, 43));
    private readonly Brush _activeTextBrush = Brushes.White;
    private readonly DispatcherTimer _refreshTimer;
    private INotifyCollectionChanged? _observedPressedCollection;
    private HashSet<string> _pendingPressedButtons = new(StringComparer.OrdinalIgnoreCase);
    private ControllerTypeModel _pendingControllerType = ControllerTypeModel.None;
    private bool _refreshRequested = true;

    public DevicePreviewControl()
    {
        InitializeComponent();
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _refreshTimer.Tick += (_, _) => FlushPendingRefresh();
        _refreshTimer.Start();
        Unloaded += (_, _) => _refreshTimer.Stop();
        UpdateLabels();
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

    private static void OnVisualStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DevicePreviewControl)d;
        control._pendingControllerType = control.ControllerType;
        control.RequestRefresh();
    }

    private static void OnPressedButtonsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DevicePreviewControl)d;
        if (control._observedPressedCollection is not null)
        {
            control._observedPressedCollection.CollectionChanged -= control.OnPressedCollectionChanged;
        }

        control._observedPressedCollection = e.NewValue as INotifyCollectionChanged;
        if (control._observedPressedCollection is not null)
        {
            control._observedPressedCollection.CollectionChanged += control.OnPressedCollectionChanged;
        }

        control.CapturePendingPressedButtons();
        control.RequestRefresh();
    }

    private void OnPressedCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        CapturePendingPressedButtons();
        RequestRefresh();
    }

    private void UpdateLabels()
    {
        if (ControllerType == ControllerTypeModel.DualShock4)
        {
            ApplyPlayStationLabels("Share");
            return;
        }

        if (ControllerType == ControllerTypeModel.DualSenseDse)
        {
            ApplyPlayStationLabels("Create");
            return;
        }

        LabelA.Text = "A";
        LabelB.Text = "B";
        LabelX.Text = "X";
        LabelY.Text = "Y";
        LabelLB.Text = "LB";
        LabelRB.Text = "RB";
        LabelLT.Text = "LT";
        LabelRT.Text = "RT";
        LabelBack.Text = "Back";
        LabelStart.Text = "Start";
    }

    private void ApplyPlayStationLabels(string shareLabel)
    {
        LabelA.Text = "×";
        LabelB.Text = "○";
        LabelX.Text = "□";
        LabelY.Text = "△";
        LabelLB.Text = "L1";
        LabelRB.Text = "R1";
        LabelLT.Text = "L2";
        LabelRT.Text = "R2";
        LabelBack.Text = shareLabel;
        LabelStart.Text = "Options";
    }

    private void UpdateHighlights()
    {
        var pressed = _pendingPressedButtons;
        var disconnected = _pendingControllerType == ControllerTypeModel.None;

        ControllerShell.Fill = disconnected
            ? new SolidColorBrush(Color.FromRgb(248, 246, 247))
            : Brushes.White;

        SetShape(ButtonA, LabelA, pressed.Contains("A"), disconnected);
        SetShape(ButtonB, LabelB, pressed.Contains("B"), disconnected);
        SetShape(ButtonX, LabelX, pressed.Contains("X"), disconnected);
        SetShape(ButtonY, LabelY, pressed.Contains("Y"), disconnected);
        SetBorder(ButtonLB, LabelLB, pressed.Contains("LB"), disconnected, _idleBrush);
        SetBorder(ButtonRB, LabelRB, pressed.Contains("RB"), disconnected, _idleBrush);
        SetBorder(ButtonLT, LabelLT, pressed.Contains("LT"), disconnected, _triggerIdleBrush);
        SetBorder(ButtonRT, LabelRT, pressed.Contains("RT"), disconnected, _triggerIdleBrush);
        SetBorder(ButtonBack, LabelBack, pressed.Contains("Back"), disconnected, _idleBrush);
        SetBorder(ButtonStart, LabelStart, pressed.Contains("Start"), disconnected, _idleBrush);
        SetShape(ButtonDPadUp, null, pressed.Contains("DPadUp"), disconnected);
        SetShape(ButtonDPadDown, null, pressed.Contains("DPadDown"), disconnected);
        SetShape(ButtonDPadLeft, null, pressed.Contains("DPadLeft"), disconnected);
        SetShape(ButtonDPadRight, null, pressed.Contains("DPadRight"), disconnected);
        SetShape(ButtonLeftStick, null, pressed.Contains("LeftStick"), disconnected);
        SetShape(ButtonRightStick, null, pressed.Contains("RightStick"), disconnected);
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

    private void CapturePendingPressedButtons()
    {
        _pendingPressedButtons = GetPressedCanonicalButtons();
    }

    private void RequestRefresh()
    {
        CapturePendingPressedButtons();
        _pendingControllerType = ControllerType;
        _refreshRequested = true;
    }

    private void FlushPendingRefresh()
    {
        if (!_refreshRequested)
        {
            return;
        }

        _refreshRequested = false;
        UpdateLabels();
        UpdateHighlights();
    }

    private void SetShape(Shape shape, TextBlock? label, bool active, bool disconnected)
    {
        shape.Fill = disconnected ? _disconnectedBrush : active ? _activeBrush : _idleBrush;
        if (label is not null)
        {
            label.Foreground = active && !disconnected ? _activeTextBrush : _textBrush;
        }
    }

    private void SetBorder(Border border, TextBlock label, bool active, bool disconnected, Brush idleBrush)
    {
        border.Background = disconnected ? _disconnectedBrush : active ? _activeBrush : idleBrush;
        label.Foreground = active && !disconnected ? _activeTextBrush : _textBrush;
    }
}
