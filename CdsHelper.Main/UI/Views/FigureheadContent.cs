using System.Windows;
using System.Windows.Controls;

namespace CdsHelper.Main.UI.Views;

public class FigureheadContent : ContentControl
{
    static FigureheadContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(FigureheadContent),
            new FrameworkPropertyMetadata(typeof(FigureheadContent)));
    }
}
