using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DouDouDeDou.Models;
using ControllerTypeModel = DouDouDeDou.Models.ControllerType;

namespace DouDouDeDou.Controls;

public partial class ControllerPreviewControl : UserControl
{
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

    private readonly Brush _idleBrush = new SolidColorBrush(Color.FromRgb(249, 237, 241));
    private readonly Brush _triggerIdleBrush = new SolidColorBrush(Color.FromRgb(245, 225, 232));
    private readonly Brush _activeBrush = new SolidColorBrush(Color.FromRgb(233, 133, 163));
    private readonly Brush _disconnectedBrush = new SolidColorBrush(Color.FromRgb(231, 224, 227));
    private readonly Brush _textBrush = new SolidColorBrush(Color.FromRgb(43, 43, 43));
    private readonly Brush _activeTextBrush = Brushes.White;
    private readonly DispatcherTimer _refreshTimer;

    private INotifyCollectionChanged? _observedPressedCollection;
    private HashSet<string> _pendingPressedButtons = new(StringComparer.OrdinalIgnoreCase);
    private ControllerTypeModel _pendingControllerType = ControllerTypeModel.None;
    private bool _refreshRequested = true;

    public ControllerPreviewControl()
    {
        InitializeComponent();
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _refreshTimer.Tick += (_, _) => FlushPendingRefresh();
        _refreshTimer.Start();
        Unloaded += (_, _) => _refreshTimer.Stop();
        RegisterButtonClicks();
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

    private static void OnVisualStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ControllerPreviewControl)d;
        control.RequestRefresh();
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
        UpdateLabels();
        UpdateHighlights();
    }

    private void RegisterButtonClicks()
    {
        RegisterButton(ButtonA, "A");
        RegisterButton(LabelA, "A");
        RegisterButton(ButtonB, "B");
        RegisterButton(LabelB, "B");
        RegisterButton(ButtonX, "X");
        RegisterButton(LabelX, "X");
        RegisterButton(ButtonY, "Y");
        RegisterButton(LabelY, "Y");
        RegisterButton(ButtonLB, "LB / L1");
        RegisterButton(ButtonRB, "RB / R1");
        RegisterButton(ButtonLT, "LT / L2");
        RegisterButton(ButtonRT, "RT / R2");
        RegisterButton(ButtonBack, "Back / Share / Create");
        RegisterButton(ButtonStart, "Start / Options");
        RegisterButton(ButtonGuide, "PS");
        RegisterButton(LabelGuide, "PS");
        RegisterButton(ButtonTouchpad, "Touchpad");
        RegisterButton(ButtonDPadUp, "DPadUp");
        RegisterButton(ButtonDPadDown, "DPadDown");
        RegisterButton(ButtonDPadLeft, "DPadLeft");
        RegisterButton(ButtonDPadRight, "DPadRight");
        RegisterButton(ButtonLeftStick, "LeftStick");
        RegisterButton(ButtonRightStick, "RightStick");
    }

    private void RegisterButton(FrameworkElement element, string sourceButton)
    {
        element.Tag = sourceButton;
        element.Cursor = Cursors.Hand;
        element.ToolTip = $"点击设置 {sourceButton} 映射";
        element.MouseLeftButtonUp += OnControllerButtonClick;
    }

    private void OnControllerButtonClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string sourceButton })
        {
            return;
        }

        var command = ButtonCommand;
        if (string.Equals(sourceButton, "PS", StringComparison.OrdinalIgnoreCase)
            && _pendingControllerType != ControllerTypeModel.DualShock4
            && _pendingControllerType != ControllerTypeModel.DualSenseDse)
        {
            return;
        }

        if (command?.CanExecute(sourceButton) == true)
        {
            command.Execute(sourceButton);
            e.Handled = true;
        }
    }

    private void UpdateLabels()
    {
        var isPlayStation =
            _pendingControllerType == ControllerTypeModel.DualShock4
            || _pendingControllerType == ControllerTypeModel.DualSenseDse;

        ButtonGuide.Visibility = isPlayStation ? Visibility.Visible : Visibility.Collapsed;
        LabelGuide.Visibility = isPlayStation ? Visibility.Visible : Visibility.Collapsed;
        ButtonTouchpad.Visibility = isPlayStation ? Visibility.Visible : Visibility.Collapsed;

        if (_pendingControllerType == ControllerTypeModel.DualShock4)
        {
            ApplyPlayStationLabels("Share");
            return;
        }

        if (_pendingControllerType == ControllerTypeModel.DualSenseDse)
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
        LabelGuide.Text = "";
        LabelTouchpad.Text = "Touch";
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
        LabelGuide.Text = "PS";
        LabelTouchpad.Text = "Touch";
    }

    private void UpdateHighlights()
    {
        var pressed = _pendingPressedButtons;
        var disconnected = _pendingControllerType == ControllerTypeModel.None;

        BodyPath.Fill = disconnected ? Brushes.WhiteSmoke : new SolidColorBrush(Color.FromRgb(255, 253, 254));
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
        SetShape(ButtonGuide, LabelGuide, pressed.Contains("PS"), disconnected);
        SetBorder(ButtonTouchpad, LabelTouchpad, pressed.Contains("Touchpad"), disconnected, _idleBrush);
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
