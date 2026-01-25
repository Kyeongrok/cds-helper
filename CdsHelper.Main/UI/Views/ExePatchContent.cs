using System.Windows;
using System.Windows.Controls;
using CdsHelper.Main.Local.ViewModels;

namespace CdsHelper.Main.UI.Views;

public class ExePatchContent : ContentControl
{
    static ExePatchContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ExePatchContent),
            new FrameworkPropertyMetadata(typeof(ExePatchContent)));
    }

    public ExePatchContent()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ExePatchContentViewModel)
            return;

        DataContext = new ExePatchContentViewModel();
    }
}
