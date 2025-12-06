using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CdsHelper.Support.UI.Units;

public class AreaMarker : Border
{
    public static readonly DependencyProperty XProperty =
        DependencyProperty.Register(nameof(X), typeof(double), typeof(AreaMarker),
            new PropertyMetadata(0.0, OnPositionChanged));

    public static readonly DependencyProperty YProperty =
        DependencyProperty.Register(nameof(Y), typeof(double), typeof(AreaMarker),
            new PropertyMetadata(0.0, OnPositionChanged));

    public double X
    {
        get => (double)GetValue(XProperty);
        set => SetValue(XProperty, value);
    }

    public double Y
    {
        get => (double)GetValue(YProperty);
        set => SetValue(YProperty, value);
    }

    public AreaMarker()
    {
        Background = Brushes.Red;
        Opacity = 0.5;
    }

    public AreaMarker(double x, double y, double width, double height, Brush? fill = null)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Background = fill ?? Brushes.Red;
        Opacity = 0.5;

        Canvas.SetLeft(this, x);
        Canvas.SetTop(this, y);
    }

    private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AreaMarker marker)
        {
            Canvas.SetLeft(marker, marker.X);
            Canvas.SetTop(marker, marker.Y);
        }
    }
}
