using System.Windows;
using System.Windows.Controls;

namespace AtomicDriftTuner.Controls;

/// <summary>Readable cards that form fewer columns as the available width decreases.</summary>
public sealed class AdaptivePanel : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(AdaptivePanel),
        new FrameworkPropertyMetadata(300d, FrameworkPropertyMetadataOptions.AffectsMeasure),
        value => value is double number && double.IsFinite(number) && number > 0);
    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(AdaptivePanel),
        new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsMeasure),
        value => value is int number && number > 0);
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(AdaptivePanel),
        new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.AffectsMeasure),
        value => value is double number && double.IsFinite(number) && number >= 0);

    public double MinItemWidth { get => (double)GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }
    public int MaxColumns { get => (int)GetValue(MaxColumnsProperty); set => SetValue(MaxColumnsProperty, value); }
    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    private UIElement[] VisibleChildren() => InternalChildren.Cast<UIElement>()
        .Where(child => child.Visibility != Visibility.Collapsed).ToArray();

    private int ColumnCount(double width, int count) => Math.Max(1, Math.Min(Math.Min(MaxColumns, count),
        double.IsFinite(width) ? (int)Math.Floor((width + Spacing) / (MinItemWidth + Spacing)) : MaxColumns));

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = VisibleChildren();
        if (children.Length == 0) return new Size();
        var columns = ColumnCount(availableSize.Width, children.Length);
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : columns * (MinItemWidth + Spacing) - Spacing;
        var cellWidth = Math.Max(0, (width - (columns - 1) * Spacing) / columns);
        foreach (var child in children) child.Measure(new Size(cellWidth, double.PositiveInfinity));
        var height = 0d;
        for (var start = 0; start < children.Length; start += columns)
        {
            if (start > 0) height += Spacing;
            height += children.Skip(start).Take(columns).Max(child => child.DesiredSize.Height);
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = VisibleChildren();
        var columns = ColumnCount(finalSize.Width, children.Length);
        var cellWidth = Math.Max(0, (finalSize.Width - (columns - 1) * Spacing) / columns);
        var y = 0d;
        for (var start = 0; start < children.Length; start += columns)
        {
            var row = children.Skip(start).Take(columns).ToArray();
            var height = row.Max(child => child.DesiredSize.Height);
            for (var column = 0; column < row.Length; column++)
                row[column].Arrange(new Rect(column * (cellWidth + Spacing), y, cellWidth, height));
            y += height + Spacing;
        }
        return finalSize;
    }
}
