using System.IO;
using System.Text;
using System.Windows;
using CdsHelper.Form.Local.ViewModels;
using CdsHelper.Form.UI.Views;
using CdsHelper.Support.Local.Helpers;
using Prism.DryIoc;
using Prism.Ioc;

namespace cds_helper;

public partial class App : PrismApplication
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // EUC-KR 인코딩 지원 등록
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        base.OnStartup(e);

        // CityService 초기화
        var cityService = Container.Resolve<CityService>();
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var dbPath = Path.Combine(basePath, "cds-helper.db");
        var jsonPath = Path.Combine(basePath, "cities.json");
        Task.Run(() => cityService.InitializeAsync(dbPath, jsonPath)).GetAwaiter().GetResult();
    }

    protected override Window CreateShell()
    {
        return Container.Resolve<CdsHelperWindow>();
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
