using System.Windows;
using System.Windows.Controls;

namespace CdsHelper.Main.UI.Views;

public class ItemContent : ContentControl
{
    static ItemContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ItemContent),
            new FrameworkPropertyMetadata(typeof(ItemContent)));
    }
}
