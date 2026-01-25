using System.Windows;
using System.Windows.Controls;
using CdsHelper.Main.Local.ViewModels;

namespace CdsHelper.Main.UI.Views;

public class ExeAppearPatchContent : ContentControl
{
    static ExeAppearPatchContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ExeAppearPatchContent),
            new FrameworkPropertyMetadata(typeof(ExeAppearPatchContent)));
    }

    public ExeAppearPatchContent()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ExeAppearPatchContentViewModel)
            return;

        DataContext = new ExeAppearPatchContentViewModel();
    }
}
