using System.Windows;
using System.Windows.Controls;
using CdsHelper.Main.Local.ViewModels;
using CdsHelper.Support.Local.Helpers;
using Prism.Ioc;

namespace CdsHelper.Main.UI.Views;

public class DiscoveryContent : ContentControl
{
    static DiscoveryContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(DiscoveryContent),
            new FrameworkPropertyMetadata(typeof(DiscoveryContent)));
    }

    public DiscoveryContent()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DiscoveryContentViewModel)
            return;

        var discoveryService = ContainerLocator.Container.Resolve<DiscoveryService>();
        DataContext = new DiscoveryContentViewModel(discoveryService);
    }
}
