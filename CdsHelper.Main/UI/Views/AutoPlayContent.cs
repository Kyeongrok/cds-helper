using System.Windows;
using System.Windows.Controls;
using CdsHelper.Main.Local.ViewModels;

namespace CdsHelper.Main.UI.Views;

public class AutoPlayContent : ContentControl
{
    static AutoPlayContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(AutoPlayContent),
            new FrameworkPropertyMetadata(typeof(AutoPlayContent)));
    }

    public AutoPlayContent()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is AutoPlayContentViewModel)
            return;

        DataContext = new AutoPlayContentViewModel();
    }
}
