using System.Reflection;
using System.Windows;
using System.Windows.Controls;
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
[TemplatePart(Name = PART_EventQueueMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_DbTableViewerMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_HelpMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_AccordionMenu, Type = typeof(NavigationMenu))]
[TemplatePart(Name = PART_ContentRegion, Type = typeof(ContentControl))]
[TemplatePart(Name = PART_MenuToggleButton, Type = typeof(Button))]
[TemplatePart(Name = PART_MenuColumn, Type = typeof(ColumnDefinition))]
public class CdsHelperWindow : CdsWindow
{
    private const string PART_SettingsMenu = "PART_SettingsMenu";
    private const string PART_EventQueueMenu = "PART_EventQueueMenu";
    private const string PART_DbTableViewerMenu = "PART_DbTableViewerMenu";
    private const string PART_HelpMenu = "PART_HelpMenu";
    private const string PART_AccordionMenu = "PART_AccordionMenu";
    private const string PART_ContentRegion = "PART_ContentRegion";
    private const string PART_MenuToggleButton = "PART_MenuToggleButton";
    private const string PART_MenuColumn = "PART_MenuColumn";

    private CdsHelperViewModel? _viewModel;
    private readonly IRegionManager _regionManager;
    private ColumnDefinition? _menuColumn;
    private Button? _menuToggleButton;
    private NavigationMenu? _accordionMenu;
    private bool _isMenuCollapsed;

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

        _menuColumn = GetTemplateChild(PART_MenuColumn) as ColumnDefinition;
        _menuToggleButton = GetTemplateChild(PART_MenuToggleButton) as Button;
        if (_menuToggleButton != null)
        {
            _menuToggleButton.Click += OnMenuToggleClick;
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

    private void OnMenuToggleClick(object sender, RoutedEventArgs e)
    {
        if (_menuColumn == null || _accordionMenu == null || _menuToggleButton == null) return;

        _isMenuCollapsed = !_isMenuCollapsed;
        _accordionMenu.IsMinimized = _isMenuCollapsed;

        if (_isMenuCollapsed)
        {
            // 메뉴 최소화 (아이콘만)
            _menuColumn.Width = new GridLength(45);
            _menuToggleButton.Margin = new Thickness(-12, 5, 0, 0);

            // 화살표 방향 변경 (오른쪽으로)
            UpdateToggleArrow(false);
        }
        else
        {
            // 메뉴 펼치기
            _menuColumn.Width = new GridLength(200);
            _menuToggleButton.Margin = new Thickness(-12, 5, 0, 0);

            // 화살표 방향 변경 (왼쪽으로)
            UpdateToggleArrow(true);
        }
    }

    private void UpdateToggleArrow(bool pointLeft)
    {
        if (_menuToggleButton?.Template.FindName("arrow", _menuToggleButton) is Path arrow)
        {
            // 왼쪽: M 6 0 L 0 5 L 6 10, 오른쪽: M 0 0 L 6 5 L 0 10
            arrow.Data = pointLeft
                ? Geometry.Parse("M 6 0 L 0 5 L 6 10")
                : Geometry.Parse("M 0 0 L 6 5 L 0 10");
        }
    }
}
