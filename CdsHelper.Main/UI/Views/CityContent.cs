using System.Windows;
using System.Windows.Controls;
using CdsHelper.Main.Local.ViewModels;
using CdsHelper.Support.Local.Helpers;
using Prism.Ioc;

namespace CdsHelper.Main.UI.Views;

public class CityContent : ContentControl
{
    static CityContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(CityContent),
            new FrameworkPropertyMetadata(typeof(CityContent)));
    }

    public CityContent()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is CityContentViewModel)
            return;

        var cityService = ContainerLocator.Container.Resolve<CityService>();
        DataContext = new CityContentViewModel(cityService);
    }
}
