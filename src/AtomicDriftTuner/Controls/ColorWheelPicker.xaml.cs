using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AtomicDriftTuner.Controls;

public sealed class ColorWheelChangedEventArgs : EventArgs
{
    public Color Color { get; }
    public ColorWheelChangedEventArgs(Color color) => Color = color;
}

public partial class ColorWheelPicker : UserControl
{
    private const int BitmapSize = 260;
    private double _hue;
    private double _saturation;
    private double _value = 1.0;
    private bool _dragging;
    private bool _updating;

    public event EventHandler<ColorWheelChangedEventArgs>? ColorChanged;

    public Color SelectedColor => HsvToRgb(_hue, _saturation, _value);

    public ColorWheelPicker()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RenderWheel();
            UpdateMarkerAndSwatch();
        };
    }

    public void SetColor(Color color, bool raiseEvent = false)
    {
        RgbToHsv(color, out _hue, out _saturation, out _value);
        _updating = true;
        ValueSlider.Value = Math.Clamp(_value * 100.0, ValueSlider.Minimum, ValueSlider.Maximum);
        _updating = false;
        RenderWheel();
        UpdateMarkerAndSwatch();
        if (raiseEvent) RaiseColorChanged();
    }

    private void WheelCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        WheelCanvas.CaptureMouse();
        UpdateFromPoint(e.GetPosition(WheelImage));
    }

    private void WheelCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging && e.LeftButton == MouseButtonState.Pressed)
            UpdateFromPoint(e.GetPosition(WheelImage));
    }

    private void WheelCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        WheelCanvas.ReleaseMouseCapture();
        UpdateFromPoint(e.GetPosition(WheelImage));
    }

    private void UpdateFromPoint(Point p)
    {
        var radius = BitmapSize / 2.0;
        var dx = p.X - radius;
        var dy = p.Y - radius;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance > radius)
        {
            var scale = radius / distance;
            dx *= scale;
            dy *= scale;
            distance = radius;
        }

        _saturation = Math.Clamp(distance / radius, 0.0, 1.0);
        var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        if (angle < 0) angle += 360.0;
        _hue = angle;
        UpdateMarkerAndSwatch();
        RaiseColorChanged();
    }

    private void ValueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || !IsLoaded) return;
        _value = ValueSlider.Value / 100.0;
        RenderWheel();
        UpdateMarkerAndSwatch();
        RaiseColorChanged();
    }

    private void RenderWheel()
    {
        if (!IsLoaded && WheelImage == null) return;
        var pixels = new byte[BitmapSize * BitmapSize * 4];
        var radius = BitmapSize / 2.0;

        for (var y = 0; y < BitmapSize; y++)
        {
            for (var x = 0; x < BitmapSize; x++)
            {
                var dx = (x + 0.5) - radius;
                var dy = (y + 0.5) - radius;
                var r = Math.Sqrt(dx * dx + dy * dy);
                var offset = (y * BitmapSize + x) * 4;

                if (r > radius)
                {
                    pixels[offset + 3] = 0;
                    continue;
                }

                var sat = Math.Clamp(r / radius, 0.0, 1.0);
                var hue = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                if (hue < 0) hue += 360.0;
                var c = HsvToRgb(hue, sat, _value);
                pixels[offset] = c.B;
                pixels[offset + 1] = c.G;
                pixels[offset + 2] = c.R;
                pixels[offset + 3] = 255;
            }
        }

        var bitmap = new WriteableBitmap(BitmapSize, BitmapSize, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, BitmapSize, BitmapSize), pixels, BitmapSize * 4, 0);
        WheelImage.Source = bitmap;
    }

    private void UpdateMarkerAndSwatch()
    {
        var radius = BitmapSize / 2.0;
        var rad = _hue * Math.PI / 180.0;
        var distance = _saturation * radius;
        var x = 5 + radius + Math.Cos(rad) * distance - Marker.Width / 2.0;
        var y = 5 + radius + Math.Sin(rad) * distance - Marker.Height / 2.0;
        Canvas.SetLeft(Marker, x);
        Canvas.SetTop(Marker, y);

        var color = SelectedColor;
        Swatch.Background = new SolidColorBrush(color);
        HexText.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private void RaiseColorChanged() =>
        ColorChanged?.Invoke(this, new ColorWheelChangedEventArgs(SelectedColor));

    private static Color HsvToRgb(double hue, double saturation, double value)
    {
        hue = ((hue % 360.0) + 360.0) % 360.0;
        saturation = Math.Clamp(saturation, 0.0, 1.0);
        value = Math.Clamp(value, 0.0, 1.0);

        var c = value * saturation;
        var x = c * (1 - Math.Abs((hue / 60.0) % 2 - 1));
        var m = value - c;
        double r1, g1, b1;

        if (hue < 60) (r1, g1, b1) = (c, x, 0);
        else if (hue < 120) (r1, g1, b1) = (x, c, 0);
        else if (hue < 180) (r1, g1, b1) = (0, c, x);
        else if (hue < 240) (r1, g1, b1) = (0, x, c);
        else if (hue < 300) (r1, g1, b1) = (x, 0, c);
        else (r1, g1, b1) = (c, 0, x);

        return Color.FromRgb(
            (byte)Math.Round((r1 + m) * 255),
            (byte)Math.Round((g1 + m) * 255),
            (byte)Math.Round((b1 + m) * 255));
    }

    private static void RgbToHsv(Color color, out double h, out double s, out double v)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        if (delta == 0) h = 0;
        else if (max == r) h = 60 * (((g - b) / delta) % 6);
        else if (max == g) h = 60 * (((b - r) / delta) + 2);
        else h = 60 * (((r - g) / delta) + 4);
        if (h < 0) h += 360;

        s = max == 0 ? 0 : delta / max;
        v = max;
    }
}
