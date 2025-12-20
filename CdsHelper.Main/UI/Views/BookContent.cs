using System.Windows;
using System.Windows.Controls;
using CdsHelper.Main.Local.ViewModel;
using CdsHelper.Support.Local.Helpers;
using Prism.Events;
using Prism.Ioc;

namespace CdsHelper.Main.UI.Views;

public class BookContent : ContentControl
{
    static BookContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(BookContent),
            new FrameworkPropertyMetadata(typeof(BookContent)));
    }

    public BookContent()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is BookContentViewModel)
            return;

        var bookService = ContainerLocator.Container.Resolve<BookService>();
        var cityService = ContainerLocator.Container.Resolve<CityService>();
        var saveDataService = ContainerLocator.Container.Resolve<SaveDataService>();
        var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
        DataContext = new BookContentViewModel(bookService, cityService, saveDataService, eventAggregator);
    }
}
