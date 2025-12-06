using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CdsHelper.Support.UI.Units;

public class Marker : Border
{
    public static readonly DependencyProperty XProperty =
        DependencyProperty.Register(nameof(X), typeof(double), typeof(Marker),
            new PropertyMetadata(0.0, OnPositionChanged));

    public static readonly DependencyProperty YProperty =
        DependencyProperty.Register(nameof(Y), typeof(double), typeof(Marker),
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

    public Marker()
    {
        Width = 8;
        Height = 8;
        CornerRadius = new CornerRadius(4);
        Background = Brushes.DodgerBlue;
        BorderBrush = Brushes.DarkBlue;
        BorderThickness = new Thickness(1);
    }

    public Marker(double x, double y, double size = 8, Brush? fill = null)
    {
        X = x;
        Y = y;
        Width = size;
        Height = size;
        CornerRadius = new CornerRadius(size / 2);
        Background = fill ?? Brushes.DodgerBlue;
        BorderBrush = Brushes.DarkBlue;
        BorderThickness = new Thickness(1);

        Canvas.SetLeft(this, x - size / 2);
        Canvas.SetTop(this, y - size / 2);
    }

    private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Marker marker)
        {
            Canvas.SetLeft(marker, marker.X - marker.Width / 2);
            Canvas.SetTop(marker, marker.Y - marker.Height / 2);
        }
    }
}
