using System.Windows;
using System.Windows.Controls;

namespace CdsHelper.Main.UI.Views;

public class BookContent : ContentControl
{
    static BookContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(BookContent),
            new FrameworkPropertyMetadata(typeof(BookContent)));
    }
}
