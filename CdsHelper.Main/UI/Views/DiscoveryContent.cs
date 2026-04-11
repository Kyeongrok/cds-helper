using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CdsHelper.Main.Local.ViewModels;
using CdsHelper.Support.Local.Helpers;
using Prism.Events;
using Prism.Ioc;

namespace CdsHelper.Main.UI.Views;

public class DiscoveryContent : ContentControl
{
    private DataGrid? _grid;
    private DiscoveryService? _discoveryService;

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

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _grid = GetTemplateChild("PART_DiscoveryGrid") as DataGrid;
        if (_grid != null)
            _grid.MouseDoubleClick += OnGridDoubleClick;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DiscoveryContentViewModel)
            return;

        _discoveryService = ContainerLocator.Container.Resolve<DiscoveryService>();
        var saveDataService = ContainerLocator.Container.Resolve<SaveDataService>();
        var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
        DataContext = new DiscoveryContentViewModel(_discoveryService, saveDataService, eventAggregator);
    }

    private async void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_grid?.SelectedItem is not DiscoveryDisplayItem item) return;
        if (_discoveryService == null) return;

        var dialog = new EditDiscoveryDialog(item)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            await _discoveryService.UpdateCoordinateAsync(
                item.Id, dialog.LatFrom, dialog.LatTo, dialog.LonFrom, dialog.LonTo);

            // UI 갱신
            item.LatFrom = dialog.LatFrom;
            item.LatTo = dialog.LatTo;
            item.LonFrom = dialog.LonFrom;
            item.LonTo = dialog.LonTo;
            item.CoordinateDisplay = DiscoveryContentViewModel.FormatCoordinatePublic(
                dialog.LatFrom, dialog.LatTo, dialog.LonFrom, dialog.LonTo);
            item.OnPropertyChanged(nameof(item.CoordinateDisplay));
        }
    }
}
