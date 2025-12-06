using System.Windows;
using System.Windows.Controls;

namespace CdsHelper.Main.UI.Views;

public class PatronContent : ContentControl
{
    static PatronContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(PatronContent),
            new FrameworkPropertyMetadata(typeof(PatronContent)));
    }
}
