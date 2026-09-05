using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AtomicDriftTuner.Controls;

public sealed class ColorWheelChangedEventArgs : EventArgs
{
    public Color Color { get; }

    public ColorWheelChangedEventArgs(
        Color color)
    {
        Color =
            color;
    }
}

public partial class ColorWheelPicker : UserControl
{
    private const int BitmapSize =
        260;

    private const double WheelImageOffset =
        5.0;

    private const double KeyboardHueStep =
        2.0;

    private const double KeyboardSaturationStep =
        0.02;

    public static readonly DependencyProperty SelectedColorProperty =
        DependencyProperty.Register(
            nameof(SelectedColor),
            typeof(Color),
            typeof(ColorWheelPicker),
            new FrameworkPropertyMetadata(
                Colors.White,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedColorPropertyChanged));

    private double _hue;
    private double _saturation;
    private double _value =
        1.0;

    private byte _alpha =
        255;

    private bool _dragging;
    private bool _updatingValueSlider;
    private bool _synchronizingSelectedColor;

    private TouchDevice? _activeTouchDevice;

    private WriteableBitmap? _wheelBitmap;
    private byte[]? _fullValuePixels;
    private byte[]? _renderPixels;

    public event EventHandler<ColorWheelChangedEventArgs>? ColorChanged;

    public Color SelectedColor
    {
        get =>
            (Color)GetValue(
                SelectedColorProperty);

        set =>
            SetValue(
                SelectedColorProperty,
                value);
    }

    public ColorWheelPicker()
    {
        InitializeComponent();

        Loaded +=
            ColorWheelPicker_Loaded;

        Unloaded +=
            ColorWheelPicker_Unloaded;
    }

    public void SetColor(
        Color color,
        bool raiseEvent = false)
    {
        ApplyColor(
            color);

        SynchronizeSelectedColor(
            color);

        if (raiseEvent)
        {
            RaiseColorChanged();
        }
    }

    private static void OnSelectedColorPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (
            dependencyObject is not ColorWheelPicker picker ||
            picker._synchronizingSelectedColor ||
            eventArgs.NewValue is not Color color)
        {
            return;
        }

        picker.ApplyColor(
            color);
    }

    private void ColorWheelPicker_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        ApplyColor(
            SelectedColor);
    }

    private void ColorWheelPicker_Unloaded(
        object sender,
        RoutedEventArgs e)
    {
        _dragging =
            false;

        if (WheelCanvas.IsMouseCaptured)
        {
            WheelCanvas.ReleaseMouseCapture();
        }

        if (_activeTouchDevice is not null)
        {
            WheelCanvas.ReleaseTouchCapture(
                _activeTouchDevice);

            _activeTouchDevice =
                null;
        }
    }

    private void ApplyColor(
        Color color)
    {
        _alpha =
            color.A;

        RgbToHsv(
            color,
            out _hue,
            out _saturation,
            out _value);

        _value =
            Math.Clamp(
                _value,
                0.0,
                1.0);

        _updatingValueSlider =
            true;

        try
        {
            ValueSlider.Value =
                Math.Clamp(
                    _value * 100.0,
                    ValueSlider.Minimum,
                    ValueSlider.Maximum);
        }
        finally
        {
            _updatingValueSlider =
                false;
        }

        RenderWheel();
        UpdateMarkerAndSwatch();
    }

    private void WheelCanvas_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_activeTouchDevice is not null)
        {
            return;
        }

        WheelCanvas.Focus();

        _dragging =
            WheelCanvas.CaptureMouse();

        UpdateFromPoint(
            e.GetPosition(
                WheelImage));

        e.Handled =
            true;
    }

    private void WheelCanvas_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (
            !_dragging ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        UpdateFromPoint(
            e.GetPosition(
                WheelImage));

        e.Handled =
            true;
    }

    private void WheelCanvas_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        UpdateFromPoint(
            e.GetPosition(
                WheelImage));

        _dragging =
            false;

        if (WheelCanvas.IsMouseCaptured)
        {
            WheelCanvas.ReleaseMouseCapture();
        }

        e.Handled =
            true;
    }

    private void WheelCanvas_LostMouseCapture(
        object sender,
        MouseEventArgs e)
    {
        _dragging =
            false;
    }

    private void WheelCanvas_TouchDown(
        object sender,
        TouchEventArgs e)
    {
        if (_activeTouchDevice is not null)
        {
            return;
        }

        WheelCanvas.Focus();

        _activeTouchDevice =
            e.TouchDevice;

        WheelCanvas.CaptureTouch(
            e.TouchDevice);

        UpdateFromPoint(
            e.GetTouchPoint(
                    WheelImage)
                .Position);

        e.Handled =
            true;
    }

    private void WheelCanvas_TouchMove(
        object sender,
        TouchEventArgs e)
    {
        if (!ReferenceEquals(
                _activeTouchDevice,
                e.TouchDevice))
        {
            return;
        }

        UpdateFromPoint(
            e.GetTouchPoint(
                    WheelImage)
                .Position);

        e.Handled =
            true;
    }

    private void WheelCanvas_TouchUp(
        object sender,
        TouchEventArgs e)
    {
        if (!ReferenceEquals(
                _activeTouchDevice,
                e.TouchDevice))
        {
            return;
        }

        UpdateFromPoint(
            e.GetTouchPoint(
                    WheelImage)
                .Position);

        WheelCanvas.ReleaseTouchCapture(
            e.TouchDevice);

        _activeTouchDevice =
            null;

        e.Handled =
            true;
    }

    private void WheelCanvas_LostTouchCapture(
        object sender,
        TouchEventArgs e)
    {
        if (ReferenceEquals(
                _activeTouchDevice,
                e.TouchDevice))
        {
            _activeTouchDevice =
                null;
        }
    }

    private void WheelCanvas_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        var multiplier =
            Keyboard.Modifiers.HasFlag(
                ModifierKeys.Shift)
                ? 5.0
                : 1.0;

        var changed =
            true;

        switch (e.Key)
        {
            case Key.Left:
                _hue =
                    NormalizeHue(
                        _hue -
                        KeyboardHueStep *
                        multiplier);
                break;

            case Key.Right:
                _hue =
                    NormalizeHue(
                        _hue +
                        KeyboardHueStep *
                        multiplier);
                break;

            case Key.Up:
                _saturation =
                    Math.Clamp(
                        _saturation +
                        KeyboardSaturationStep *
                        multiplier,
                        0.0,
                        1.0);
                break;

            case Key.Down:
                _saturation =
                    Math.Clamp(
                        _saturation -
                        KeyboardSaturationStep *
                        multiplier,
                        0.0,
                        1.0);
                break;

            default:
                changed =
                    false;
                break;
        }

        if (!changed)
        {
            return;
        }

        CommitCurrentSelection();
        e.Handled =
            true;
    }

    private void UpdateFromPoint(
        Point point)
    {
        var radius =
            BitmapSize /
            2.0;

        var dx =
            point.X -
            radius;

        var dy =
            point.Y -
            radius;

        var distance =
            Math.Sqrt(
                dx * dx +
                dy * dy);

        if (distance > radius)
        {
            var scale =
                radius /
                distance;

            dx *=
                scale;

            dy *=
                scale;

            distance =
                radius;
        }

        _saturation =
            Math.Clamp(
                distance /
                radius,
                0.0,
                1.0);

        _hue =
            NormalizeHue(
                Math.Atan2(
                    dy,
                    dx) *
                180.0 /
                Math.PI);

        CommitCurrentSelection();
    }

    private void ValueSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (
            _updatingValueSlider ||
            !IsLoaded)
        {
            return;
        }

        _value =
            Math.Clamp(
                ValueSlider.Value /
                100.0,
                0.0,
                1.0);

        RenderWheel();
        CommitCurrentSelection(
            updateWheel: false);
    }

    private void CommitCurrentSelection(
        bool updateWheel = false)
    {
        if (updateWheel)
        {
            RenderWheel();
        }

        var color =
            CurrentColor();

        SynchronizeSelectedColor(
            color);

        UpdateMarkerAndSwatch();
        RaiseColorChanged();
    }

    private void SynchronizeSelectedColor(
        Color color)
    {
        _synchronizingSelectedColor =
            true;

        try
        {
            SetCurrentValue(
                SelectedColorProperty,
                color);
        }
        finally
        {
            _synchronizingSelectedColor =
                false;
        }
    }

    private void RenderWheel()
    {
        EnsureWheelResources();

        if (
            _wheelBitmap is null ||
            _fullValuePixels is null ||
            _renderPixels is null)
        {
            return;
        }

        var value =
            Math.Clamp(
                _value,
                0.0,
                1.0);

        if (value >= 0.999999)
        {
            Buffer.BlockCopy(
                _fullValuePixels,
                0,
                _renderPixels,
                0,
                _fullValuePixels.Length);
        }
        else
        {
            for (
                var offset = 0;
                offset < _fullValuePixels.Length;
                offset += 4)
            {
                _renderPixels[offset] =
                    ScaleChannel(
                        _fullValuePixels[offset],
                        value);

                _renderPixels[offset + 1] =
                    ScaleChannel(
                        _fullValuePixels[offset + 1],
                        value);

                _renderPixels[offset + 2] =
                    ScaleChannel(
                        _fullValuePixels[offset + 2],
                        value);

                _renderPixels[offset + 3] =
                    _fullValuePixels[offset + 3];
            }
        }

        _wheelBitmap.WritePixels(
            new Int32Rect(
                0,
                0,
                BitmapSize,
                BitmapSize),
            _renderPixels,
            BitmapSize * 4,
            0);

        if (!ReferenceEquals(
                WheelImage.Source,
                _wheelBitmap))
        {
            WheelImage.Source =
                _wheelBitmap;
        }
    }

    private void EnsureWheelResources()
    {
        if (
            _fullValuePixels is not null &&
            _renderPixels is not null &&
            _wheelBitmap is not null)
        {
            return;
        }

        _fullValuePixels =
            new byte[
                BitmapSize *
                BitmapSize *
                4];

        _renderPixels =
            new byte[
                _fullValuePixels.Length];

        var radius =
            BitmapSize /
            2.0;

        for (
            var y = 0;
            y < BitmapSize;
            y++)
        {
            for (
                var x = 0;
                x < BitmapSize;
                x++)
            {
                var dx =
                    x +
                    0.5 -
                    radius;

                var dy =
                    y +
                    0.5 -
                    radius;

                var distance =
                    Math.Sqrt(
                        dx * dx +
                        dy * dy);

                var offset =
                    (
                        y *
                        BitmapSize +
                        x
                    ) *
                    4;

                if (distance > radius)
                {
                    _fullValuePixels[offset + 3] =
                        0;

                    continue;
                }

                var saturation =
                    Math.Clamp(
                        distance /
                        radius,
                        0.0,
                        1.0);

                var hue =
                    NormalizeHue(
                        Math.Atan2(
                            dy,
                            dx) *
                        180.0 /
                        Math.PI);

                var color =
                    HsvToRgb(
                        hue,
                        saturation,
                        1.0,
                        255);

                _fullValuePixels[offset] =
                    color.B;

                _fullValuePixels[offset + 1] =
                    color.G;

                _fullValuePixels[offset + 2] =
                    color.R;

                _fullValuePixels[offset + 3] =
                    255;
            }
        }

        _wheelBitmap =
            new WriteableBitmap(
                BitmapSize,
                BitmapSize,
                96,
                96,
                PixelFormats.Bgra32,
                null);
    }

    private void UpdateMarkerAndSwatch()
    {
        var radius =
            BitmapSize /
            2.0;

        var radians =
            _hue *
            Math.PI /
            180.0;

        var distance =
            _saturation *
            radius;

        var x =
            WheelImageOffset +
            radius +
            Math.Cos(
                radians) *
            distance -
            Marker.Width /
            2.0;

        var y =
            WheelImageOffset +
            radius +
            Math.Sin(
                radians) *
            distance -
            Marker.Height /
            2.0;

        Canvas.SetLeft(
            Marker,
            x);

        Canvas.SetTop(
            Marker,
            y);

        var color =
            CurrentColor();

        var brush =
            new SolidColorBrush(
                color);

        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        Swatch.Background =
            brush;

        HexText.Text =
            color.A == 255
                ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
                : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private void RaiseColorChanged()
    {
        ColorChanged?.Invoke(
            this,
            new ColorWheelChangedEventArgs(
                SelectedColor));
    }

    private Color CurrentColor()
    {
        return HsvToRgb(
            _hue,
            _saturation,
            _value,
            _alpha);
    }

    private static byte ScaleChannel(
        byte channel,
        double value)
    {
        return
            (byte)Math.Clamp(
                Math.Round(
                    channel *
                    value,
                    MidpointRounding.AwayFromZero),
                0,
                255);
    }

    private static double NormalizeHue(
        double hue)
    {
        return
            (
                hue %
                360.0 +
                360.0
            ) %
            360.0;
    }

    private static Color HsvToRgb(
        double hue,
        double saturation,
        double value,
        byte alpha)
    {
        hue =
            NormalizeHue(
                hue);

        saturation =
            Math.Clamp(
                saturation,
                0.0,
                1.0);

        value =
            Math.Clamp(
                value,
                0.0,
                1.0);

        var chroma =
            value *
            saturation;

        var secondary =
            chroma *
            (
                1 -
                Math.Abs(
                    (
                        hue /
                        60.0
                    ) %
                    2 -
                    1)
            );

        var match =
            value -
            chroma;

        double red;
        double green;
        double blue;

        if (hue < 60)
        {
            (red, green, blue) =
                (
                    chroma,
                    secondary,
                    0
                );
        }
        else if (hue < 120)
        {
            (red, green, blue) =
                (
                    secondary,
                    chroma,
                    0
                );
        }
        else if (hue < 180)
        {
            (red, green, blue) =
                (
                    0,
                    chroma,
                    secondary
                );
        }
        else if (hue < 240)
        {
            (red, green, blue) =
                (
                    0,
                    secondary,
                    chroma
                );
        }
        else if (hue < 300)
        {
            (red, green, blue) =
                (
                    secondary,
                    0,
                    chroma
                );
        }
        else
        {
            (red, green, blue) =
                (
                    chroma,
                    0,
                    secondary
                );
        }

        return Color.FromArgb(
            alpha,
            ToColorByte(
                red +
                match),
            ToColorByte(
                green +
                match),
            ToColorByte(
                blue +
                match));
    }

    private static byte ToColorByte(
        double value)
    {
        return
            (byte)Math.Clamp(
                Math.Round(
                    value *
                    255.0,
                    MidpointRounding.AwayFromZero),
                0,
                255);
    }

    private static void RgbToHsv(
        Color color,
        out double hue,
        out double saturation,
        out double value)
    {
        var red =
            color.R /
            255.0;

        var green =
            color.G /
            255.0;

        var blue =
            color.B /
            255.0;

        var maximum =
            Math.Max(
                red,
                Math.Max(
                    green,
                    blue));

        var minimum =
            Math.Min(
                red,
                Math.Min(
                    green,
                    blue));

        var delta =
            maximum -
            minimum;

        if (delta == 0)
        {
            hue =
                0;
        }
        else if (maximum == red)
        {
            hue =
                60 *
                (
                    (
                        green -
                        blue
                    ) /
                    delta %
                    6
                );
        }
        else if (maximum == green)
        {
            hue =
                60 *
                (
                    (
                        blue -
                        red
                    ) /
                    delta +
                    2
                );
        }
        else
        {
            hue =
                60 *
                (
                    (
                        red -
                        green
                    ) /
                    delta +
                    4
                );
        }

        hue =
            NormalizeHue(
                hue);

        saturation =
            maximum == 0
                ? 0
                : delta /
                  maximum;

        value =
            maximum;
    }
}
