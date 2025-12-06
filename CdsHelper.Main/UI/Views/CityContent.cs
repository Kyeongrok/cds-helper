using System.Windows;
using System.Windows.Controls;

namespace CdsHelper.Main.UI.Views;

public class CityContent : ContentControl
{
    static CityContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(CityContent),
            new FrameworkPropertyMetadata(typeof(CityContent)));
    }
}
