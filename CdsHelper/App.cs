using System.IO;
using System.Text;
using System.Windows;
using CdsHelper.Api.Data;
using CdsHelper.Form.Local.Services;
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
        return Container.Resolve<CdsHelperWindow>();
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
        containerRegistry.RegisterSingleton<UpdateService>();
        containerRegistry.RegisterSingleton<CharacterService>();
        containerRegistry.RegisterSingleton<BookService>();
        containerRegistry.RegisterSingleton<CityService>();
        containerRegistry.RegisterSingleton<PatronService>();
        containerRegistry.RegisterSingleton<FigureheadService>();
        containerRegistry.RegisterSingleton<ItemService>();
        containerRegistry.RegisterSingleton<SaveDataService>();
        containerRegistry.RegisterSingleton<HintService>();
        containerRegistry.RegisterSingleton<DiscoveryService>();
        containerRegistry.RegisterSingleton<AutoPlayService>();

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
        containerRegistry.RegisterForNavigation<DiscoveryContent>();
        containerRegistry.RegisterForNavigation<ExePatchContent>();
        containerRegistry.RegisterForNavigation<AutoPlayContent>();
        containerRegistry.RegisterForNavigation<WorldMapContent>();
    }
}