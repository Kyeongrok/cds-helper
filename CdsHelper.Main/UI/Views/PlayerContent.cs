using System.Windows;
using System.Windows.Controls;
using CdsHelper.Main.Local.ViewModels;
using CdsHelper.Support.Local.Helpers;
using Prism.Events;
using Prism.Ioc;

namespace CdsHelper.Main.UI.Views;

public class PlayerContent : ContentControl
{
    static PlayerContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(PlayerContent),
            new FrameworkPropertyMetadata(typeof(PlayerContent)));
    }

    public PlayerContent()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PlayerContentViewModel)
            return;

        var saveDataService = ContainerLocator.Container.Resolve<SaveDataService>();
        var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
        DataContext = new PlayerContentViewModel(saveDataService, eventAggregator);
    }
}
