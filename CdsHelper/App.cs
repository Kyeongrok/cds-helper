using System.IO;
using System.Text;
using System.Windows;
using CdsHelper.Api.Data;
using CdsHelper.Form.Local.ViewModels;
using CdsHelper.Form.UI.Views;
using CdsHelper.Main.Local.ViewModels;
using CdsHelper.Main.UI.Views;
using CdsHelper.Support.Local.Helpers;

namespace cds_helper;

internal class App : PrismApplication
{
    protected override Window CreateShell()
    {
        // Shell 생성 전에 Services 초기화
        InitializeServices();
        return Container.Resolve<CdsHelperWindow>();
    }

    private void InitializeServices()
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var dbPath = Path.Combine(basePath, "cdshelper.db");
        var isNewDb = !File.Exists(dbPath);

        if (isNewDb)
        {
            MessageBox.Show($"DB 파일이 없어 새로 생성합니다.\n경로: {dbPath}", "DB 초기화", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        try
        {
            // CityService 초기화
            var cityService = Container.Resolve<CityService>();
            var citiesJsonPath = Path.Combine(basePath, "cities.json");
            Task.Run(() => cityService.InitializeAsync(dbPath, citiesJsonPath)).GetAwaiter().GetResult();

            // HintService 초기화 (BookHints가 Hints를 참조하므로 BookService보다 먼저)
            var hintService = Container.Resolve<HintService>();
            var hintJsonPath = Path.Combine(basePath, "hint.json");
            Task.Run(() => hintService.InitializeAsync(dbPath, hintJsonPath)).GetAwaiter().GetResult();

            // BookService 초기화
            var bookService = Container.Resolve<BookService>();
            var booksJsonPath = Path.Combine(basePath, "books.json");
            Task.Run(() => bookService.InitializeAsync(dbPath, booksJsonPath)).GetAwaiter().GetResult();

            if (isNewDb)
            {
                MessageBox.Show("DB 초기화가 완료되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"초기화 오류:\n{ex.Message}\n\n{ex.StackTrace}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // 전역 예외 핸들러 등록
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            MessageBox.Show($"UnhandledException:\n{ex?.Message}\n\n{ex?.StackTrace}", "치명적 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show($"DispatcherUnhandledException:\n{args.Exception.Message}\n\n{args.Exception.StackTrace}", "UI 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // EUC-KR 인코딩 지원 등록
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        base.OnStartup(e);
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        // AppDbContext 등록
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var dbPath = Path.Combine(basePath, "cdshelper.db");
        containerRegistry.RegisterSingleton<AppDbContext>(() => AppDbContextFactory.Create(dbPath));

        // Services 등록
        containerRegistry.RegisterSingleton<CharacterService>();
        containerRegistry.RegisterSingleton<BookService>();
        containerRegistry.RegisterSingleton<CityService>();
        containerRegistry.RegisterSingleton<PatronService>();
        containerRegistry.RegisterSingleton<FigureheadService>();
        containerRegistry.RegisterSingleton<ItemService>();
        containerRegistry.RegisterSingleton<SaveDataService>();
        containerRegistry.RegisterSingleton<HintService>();

        // ViewModel 등록
        containerRegistry.Register<CdsHelperViewModel>();
        containerRegistry.Register<PlayerContentViewModel>();

        // Navigation용 View 등록
        containerRegistry.RegisterForNavigation<CharacterContent>();
        containerRegistry.RegisterForNavigation<BookContent>();
        containerRegistry.RegisterForNavigation<CityContent>();
        containerRegistry.RegisterForNavigation<PatronContent>();
        containerRegistry.RegisterForNavigation<FigureheadContent>();
        containerRegistry.RegisterForNavigation<ItemContent>();
        containerRegistry.RegisterForNavigation<MapContent>();
        containerRegistry.RegisterForNavigation<PlayerContent>();
        containerRegistry.RegisterForNavigation<SphinxCalculatorContent>();
    }
}