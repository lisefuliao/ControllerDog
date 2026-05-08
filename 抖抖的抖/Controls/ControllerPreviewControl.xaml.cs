using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
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

    private readonly Brush _idleBrush = new SolidColorBrush(Color.FromRgb(249, 237, 241));
    private readonly Brush _triggerIdleBrush = new SolidColorBrush(Color.FromRgb(245, 225, 232));
    private readonly Brush _activeBrush = new SolidColorBrush(Color.FromRgb(233, 133, 163));
    private readonly Brush _disconnectedBrush = new SolidColorBrush(Color.FromRgb(231, 224, 227));
    private readonly Brush _textBrush = new SolidColorBrush(Color.FromRgb(43, 43, 43));
    private readonly Brush _activeTextBrush = Brushes.White;

    private INotifyCollectionChanged? _observedPressedCollection;

    public ControllerPreviewControl()
    {
        InitializeComponent();
        UpdateLabels();
        UpdateHighlights();
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
        var control = (ControllerPreviewControl)d;
        control.UpdateLabels();
        control.UpdateHighlights();
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

        control.UpdateHighlights();
    }

    private void OnPressedCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateHighlights();
    }

    private void UpdateLabels()
    {
        if (ControllerType == ControllerTypeModel.DualShock4)
        {
            LabelA.Text = "×";
            LabelB.Text = "○";
            LabelX.Text = "□";
            LabelY.Text = "△";
            LabelLB.Text = "L1";
            LabelRB.Text = "R1";
            LabelLT.Text = "L2";
            LabelRT.Text = "R2";
            LabelBack.Text = "Share";
            LabelStart.Text = "Options";
            return;
        }

        if (ControllerType == ControllerTypeModel.DualSenseDse)
        {
            LabelA.Text = "×";
            LabelB.Text = "○";
            LabelX.Text = "□";
            LabelY.Text = "△";
            LabelLB.Text = "L1";
            LabelRB.Text = "R1";
            LabelLT.Text = "L2";
            LabelRT.Text = "R2";
            LabelBack.Text = "Create";
            LabelStart.Text = "Options";
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

    private void UpdateHighlights()
    {
        var pressed = GetPressedCanonicalButtons();
        var disconnected = ControllerType == ControllerTypeModel.None;

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
