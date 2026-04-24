using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using CdsHelper.Api.Data;
using CdsHelper.Form.Local.ViewModels;
using CdsHelper.Main.UI.Views;
using CdsHelper.Navigation.UI.Views;
using CdsHelper.Support.Local.Events;
using CdsHelper.Support.Local.Settings;
using CdsHelper.Support.UI.Units;
using Prism.Events;
using Prism.Ioc;

namespace CdsHelper.Form.UI.Views;

[TemplatePart(Name = PART_SettingsMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_SphinxMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_EventQueueMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_DbTableViewerMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_HelpMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_AccordionMenu, Type = typeof(NavigationMenu))]
[TemplatePart(Name = PART_ContentRegion, Type = typeof(ContentControl))]
[TemplatePart(Name = PART_HamburgerButton, Type = typeof(Button))]
[TemplatePart(Name = PART_MenuPopup, Type = typeof(Popup))]
public class CdsHelperWindow : CdsWindow
{
    private const string PART_SettingsMenu = "PART_SettingsMenu";
    private const string PART_SphinxMenu = "PART_SphinxMenu";
    private const string PART_EventQueueMenu = "PART_EventQueueMenu";
    private const string PART_DbTableViewerMenu = "PART_DbTableViewerMenu";
    private const string PART_HelpMenu = "PART_HelpMenu";
    private const string PART_AccordionMenu = "PART_AccordionMenu";
    private const string PART_ContentRegion = "PART_ContentRegion";
    private const string PART_HamburgerButton = "PART_HamburgerButton";
    private const string PART_MenuPopup = "PART_MenuPopup";

    private CdsHelperViewModel? _viewModel;
    private readonly IRegionManager _regionManager;
    private Button? _hamburgerButton;
    private Popup? _menuPopup;
    private NavigationMenu? _accordionMenu;

    static CdsHelperWindow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(CdsHelperWindow),
            new FrameworkPropertyMetadata(typeof(CdsHelperWindow)));
    }

    public CdsHelperWindow(CdsHelperViewModel viewModel, IRegionManager regionManager)
    {
        _viewModel = viewModel;
        _regionManager = regionManager;
        DataContext = viewModel;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild(PART_SettingsMenu) is MenuItem settingsMenu)
        {
            settingsMenu.Click += OnSettingsMenuClick;
        }

        if (GetTemplateChild(PART_SphinxMenu) is MenuItem sphinxMenu)
        {
            sphinxMenu.Click += OnSphinxMenuClick;
        }

        if (GetTemplateChild(PART_EventQueueMenu) is MenuItem eventQueueMenu)
        {
            eventQueueMenu.Click += OnEventQueueMenuClick;
        }

        if (GetTemplateChild(PART_DbTableViewerMenu) is MenuItem dbTableViewerMenu)
        {
            dbTableViewerMenu.Click += OnDbTableViewerMenuClick;
        }

        if (GetTemplateChild(PART_HelpMenu) is MenuItem helpMenu)
        {
            helpMenu.Click += OnHelpMenuClick;
        }

        _accordionMenu = GetTemplateChild(PART_AccordionMenu) as NavigationMenu;
        if (_accordionMenu != null)
        {
            _accordionMenu.ItemClickCommand = new DelegateCommand<string>(OnAccordionItemClick);
            _accordionMenu.SelectItemByTag(AppSettings.DefaultView);
        }

        _menuPopup = GetTemplateChild(PART_MenuPopup) as Popup;
        _hamburgerButton = GetTemplateChild(PART_HamburgerButton) as Button;
        if (_hamburgerButton != null && _menuPopup != null)
        {
            _hamburgerButton.Click += (_, _) => _menuPopup.IsOpen = !_menuPopup.IsOpen;
        }

        // ControlTemplate 내의 ContentControl에 Region 설정
        if (GetTemplateChild(PART_ContentRegion) is ContentControl contentRegion)
        {
            RegionManager.SetRegionManager(contentRegion, _regionManager);
            RegionManager.SetRegionName(contentRegion, "MainContentRegion");

            // 초기 Navigation (설정에서 지정한 기본 뷰)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _viewModel?.NavigateToContent(AppSettings.DefaultView);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // NavigateToCityEvent 구독 - 아코디언 메뉴 동기화
        var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
        eventAggregator.GetEvent<NavigateToCityEvent>().Subscribe(OnNavigateToCity);

        // 창 로드 후 네이티브 DLL 다운로드 확인 → 업데이트 확인
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (_viewModel == null) return;
            await _viewModel.CheckAndDownloadNativeDepsAsync();
            await _viewModel.CheckForUpdateAsync();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnNavigateToCity(NavigateToCityEventArgs args)
    {
        // 아코디언 메뉴에서 지도 탭 선택
        Dispatcher.Invoke(() =>
        {
            _accordionMenu?.SelectItemByTag("MapContent");
        });
    }

    private void OnAccordionItemClick(string? viewName)
    {
        System.Diagnostics.Debug.WriteLine($"[AccordionClick] viewName: {viewName}");
        if (!string.IsNullOrEmpty(viewName))
        {
            _viewModel?.NavigateToContent(viewName);
            // 네비게이션 후 햄버거 팝업 닫기
            if (_menuPopup != null) _menuPopup.IsOpen = false;
        }
    }

    private void OnSettingsMenuClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void OnSphinxMenuClick(object sender, RoutedEventArgs e)
    {
        _viewModel?.NavigateToContent("SphinxCalculatorContent");
    }

    private void OnEventQueueMenuClick(object sender, RoutedEventArgs e)
    {
        var dialog = new EventQueueDialog
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void OnHelpMenuClick(object sender, RoutedEventArgs e)
    {
        var version = System.Reflection.Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";
        MessageBox.Show($"CDS Helper\n버전: {version}", "도움말", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnDbTableViewerMenuClick(object sender, RoutedEventArgs e)
    {
        var dbContext = ContainerLocator.Container.Resolve<AppDbContext>();
        var dialog = new DbTableViewerDialog(dbContext)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

}
