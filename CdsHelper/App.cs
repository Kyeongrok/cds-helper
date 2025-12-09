using System.IO;
using System.Text;
using System.Windows;
using CdsHelper.Form.Local.ViewModels;
using CdsHelper.Form.UI.Views;
using CdsHelper.Support.Local.Helpers;

namespace cds_helper;

internal class App : PrismApplication
{
    protected override Window CreateShell()
    {
        return Container.Resolve<CdsHelperWindow>();
    }


    protected override void OnStartup(StartupEventArgs e)
    {
        // EUC-KR 인코딩 지원 등록
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        base.OnStartup(e);

        try
        {
            // Services 초기화
            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            var dbPath = Path.Combine(basePath, "cdshelper.db");

            // CityService 초기화
            var cityService = Container.Resolve<CityService>();
            var citiesJsonPath = Path.Combine(basePath, "cities.json");
            Task.Run(() => cityService.InitializeAsync(dbPath, citiesJsonPath)).GetAwaiter().GetResult();

            // BookService 초기화
            var bookService = Container.Resolve<BookService>();
            var booksJsonPath = Path.Combine(basePath, "books.json");
            Task.Run(() => bookService.InitializeAsync(dbPath, booksJsonPath)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"초기화 오류:\n{ex.Message}\n\n{ex.StackTrace}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        // Services 등록
        containerRegistry.RegisterSingleton<CharacterService>();
        containerRegistry.RegisterSingleton<BookService>();
        containerRegistry.RegisterSingleton<CityService>();
        containerRegistry.RegisterSingleton<PatronService>();
        containerRegistry.RegisterSingleton<FigureheadService>();
        containerRegistry.RegisterSingleton<ItemService>();
        containerRegistry.RegisterSingleton<SaveDataService>();

        // ViewModel 등록
        containerRegistry.Register<CdsHelperViewModel>();
    }
}