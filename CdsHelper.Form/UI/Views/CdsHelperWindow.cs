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
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Form.UI.Views;

[TemplatePart(Name = PART_SettingsMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_SphinxMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_EventQueueMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_DbTableViewerMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_WaveBankMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_PortraitBookMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_CityCultureMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_NationEditMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_DisevEditorMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_VoyagerEditMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_PersonEditMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_ImageShrinkMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_ShipRegistryMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_ShipMapMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_HelpMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_DiscoveryMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_WorldMapMenu, Type = typeof(MenuItem))]
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
    private const string PART_WaveBankMenu = "PART_WaveBankMenu";
    private const string PART_PortraitBookMenu = "PART_PortraitBookMenu";
    private const string PART_CityCultureMenu = "PART_CityCultureMenu";
    private const string PART_NationEditMenu = "PART_NationEditMenu";
    private const string PART_DisevEditorMenu = "PART_DisevEditorMenu";
    private const string PART_VoyagerEditMenu = "PART_VoyagerEditMenu";
    private const string PART_PersonEditMenu = "PART_PersonEditMenu";
    private const string PART_ImageShrinkMenu = "PART_ImageShrinkMenu";
    private const string PART_ShipRegistryMenu = "PART_ShipRegistryMenu";
    private const string PART_ShipMapMenu = "PART_ShipMapMenu";
    private const string PART_HelpMenu = "PART_HelpMenu";
    private const string PART_DiscoveryMenu = "PART_DiscoveryMenu";
    private const string PART_WorldMapMenu = "PART_WorldMapMenu";
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

        // 게임처럼 화면 한가운데에서 뜬다. 안 정해 두면 윈도가 계단식으로 흘려 놓아
        // 구석에서 뜬다. 크기는 Themes/Views/CdsHelperWindow.xaml 의 Style 에 있다 —
        // 자리는 이쪽이다. WindowStartupLocation 은 의존 속성이 아니라 Setter 에 못 넣는다.
        //
        // CenterScreen 만으로는 어긋난다. 그 값은 창이 뜨기 전 크기로 자리를 잡는데,
        // 이 창은 템플릿이 붙으면서 크기가 한 번 더 정해지기 때문이다. 그래서 다 뜬 뒤에
        // 작업 영역(작업 표시줄을 뺀 자리) 기준으로 한 번 더 맞춘다.
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Loaded += (_, _) => CenterOnScreen();
    }

    /// <summary>작업 영역 한가운데로 옮긴다. 최대화·최소화 상태면 건드리지 않는다.</summary>
    private void CenterOnScreen()
    {
        if (WindowState != WindowState.Normal) return;

        var area = SystemParameters.WorkArea;
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;
        if (double.IsNaN(w) || double.IsNaN(h)) return;

        Left = area.Left + (area.Width - w) / 2;
        Top = area.Top + (area.Height - h) / 2;
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

        if (GetTemplateChild(PART_WaveBankMenu) is MenuItem waveBankMenu)
        {
            waveBankMenu.Click += OnWaveBankMenuClick;
        }

        if (GetTemplateChild(PART_PortraitBookMenu) is MenuItem portraitBookMenu)
        {
            portraitBookMenu.Click += OnPortraitBookMenuClick;
        }

        if (GetTemplateChild(PART_CityCultureMenu) is MenuItem cityCultureMenu)
        {
            cityCultureMenu.Click += OnCityCultureMenuClick;
        }

        if (GetTemplateChild(PART_NationEditMenu) is MenuItem nationEditMenu)
        {
            nationEditMenu.Click += OnNationEditMenuClick;
        }

        if (GetTemplateChild(PART_DisevEditorMenu) is MenuItem disevEditorMenu)
        {
            disevEditorMenu.Click += OnDisevEditorMenuClick;
        }

        if (GetTemplateChild(PART_VoyagerEditMenu) is MenuItem voyagerEditMenu)
        {
            voyagerEditMenu.Click += OnVoyagerEditMenuClick;
        }

        if (GetTemplateChild(PART_PersonEditMenu) is MenuItem personEditMenu)
        {
            personEditMenu.Click += OnPersonEditMenuClick;
        }

        if (GetTemplateChild(PART_ImageShrinkMenu) is MenuItem imageShrinkMenu)
        {
            imageShrinkMenu.Click += OnImageShrinkMenuClick;
        }

        if (GetTemplateChild(PART_ShipRegistryMenu) is MenuItem shipRegistryMenu)
        {
            shipRegistryMenu.Click += OnShipRegistryMenuClick;
        }

        if (GetTemplateChild(PART_ShipMapMenu) is MenuItem shipMapMenu)
        {
            shipMapMenu.Click += OnShipMapMenuClick;
        }

        if (GetTemplateChild(PART_HelpMenu) is MenuItem helpMenu)
        {
            helpMenu.Click += OnHelpMenuClick;
        }

        if (GetTemplateChild(PART_DiscoveryMenu) is MenuItem discoveryMenu)
        {
            discoveryMenu.Click += (_, _) => NavigateAndSync("DiscoveryContent");
        }

        if (GetTemplateChild(PART_WorldMapMenu) is MenuItem worldMapMenu)
        {
            worldMapMenu.Click += (_, _) => NavigateAndSync("WorldMapContent");
        }

        _accordionMenu = GetTemplateChild(PART_AccordionMenu) as NavigationMenu;
        if (_accordionMenu != null)
        {
            _accordionMenu.ItemClickCommand = new DelegateCommand<string>(OnAccordionItemClick);
            _accordionMenu.SelectItemByTag(AppSettings.DefaultView);
        }

        OpenShipMapIfWanted();

        _menuPopup = GetTemplateChild(PART_MenuPopup) as Popup;
        _hamburgerButton = GetTemplateChild(PART_HamburgerButton) as Button;
        if (_hamburgerButton != null && _menuPopup != null)
        {
            _hamburgerButton.Click += (_, _) => _menuPopup.IsOpen = !_menuPopup.IsOpen;
            // AllowsTransparency=True + PopupAnimation 조합에서 첫 오픈 시
            // 팝업이 화면 (0,0)에 떴다가 제 위치로 점프하는 WPF 버그 회피.
            // off+1 로 바꾼 뒤 "다음 디스패처 사이클"에 off 로 되돌려야 실제 재배치가 일어난다.
            // (같은 호출 안에서 off+1; off; 하면 두 변경이 상쇄되어 재배치가 트리거되지 않음)
            _menuPopup.Opened += (_, _) =>
            {
                var popup = _menuPopup;
                if (popup == null) return;
                var off = popup.HorizontalOffset;
                popup.HorizontalOffset = off + 1;
                popup.Dispatcher.BeginInvoke(new Action(() => popup.HorizontalOffset = off),
                    System.Windows.Threading.DispatcherPriority.Background);
            };
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

    private void NavigateAndSync(string viewName)
    {
        _viewModel?.NavigateToContent(viewName);
        _accordionMenu?.SelectItemByTag(viewName);
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

    // WAVES.CDS 에 든 게임 효과음 50개를 늘어놓고 들어 보는 창.
    private void OnWaveBankMenuClick(object sender, RoutedEventArgs e)
    {
        var dialog = new WaveBankDialog
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    // MALE.CDS · FEMALE.CDS 의 얼굴을 번호와 함께 늘어놓는 창. 게임 자료가 사람을
    // 얼굴 번호로 가리키므로(인물표 · 후원자표 · 시설 화자표) 그 번호를 찾아볼 데가 필요하다.
    private void OnPortraitBookMenuClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CdsHelper.Game.UI.Views.PortraitBookDialog
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    // 도시마다의 문화권과, 그 문화권이 부르는 시설 화자 얼굴을 맞대어 보는 창.
    private void OnCityCultureMenuClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CdsHelper.Game.UI.Views.CityCultureDialog
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    // 나라 이름·쓰는 말·수도를 고치는 창. 고친 것은 놀이에도 그대로 쓰인다.
    private void OnNationEditMenuClick(object sender, RoutedEventArgs e) =>
        CdsHelper.Game.UI.Views.NationEditDialog.Show(this);

    // DISEV.CDS 의 발견 이벤트 스크립트를 보고 고치는 창. 게임 파일을 직접 고치므로
    // 저장할 때 옆에 시각을 붙인 백업을 남긴다.
    private void OnDisevEditorMenuClick(object sender, RoutedEventArgs e) =>
        CdsHelper.Game.UI.Views.DisevEditorDialog.Show(this);

    // 역사 항해자 열넷이 언제 무엇을 채가는지 고치는 창. 이 놀이의 유일한 경쟁자다.
    private void OnVoyagerEditMenuClick(object sender, RoutedEventArgs e) =>
        CdsHelper.Game.UI.Views.VoyagerEditDialog.Show(this);

    // 세이브의 인물 281명을 고치는 창. 나라 표와 달리 세이브를 그 자리에서 고치므로
    // 처음 고칠 때 시각을 붙인 백업을 옆에 남긴다.
    private void OnPersonEditMenuClick(object sender, RoutedEventArgs e) =>
        CdsHelper.Game.UI.Views.PersonEditDialog.Show(this);

    // 그림 파일을 골라 크기·용량을 줄이는 창. 게임과는 상관없는 손도구다.
    private void OnImageShrinkMenuClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ImageShrinkDialog
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    // 이 앱이 품은 놀이의 조선소에 낼 배를 등록하는 창. 게임 EXE 는 건드리지 않는다.
    private void OnShipRegistryMenuClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ShipRegistryDialog
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    // 게임 화면처럼 지도 위에 함대만 띄우는 창. 세계지도 탭과 달리 D3D 로 그린다.
    private void OnShipMapMenuClick(object sender, RoutedEventArgs e) => OpenShipMap();

    private void OpenShipMap()
    {
        // 미궁 64 퍼즐은 CdsHelper.Maze 에 따로 있다. 그쪽이 CdsHelper.Game 을 물고
        // 있어서 게임 쪽에서 곧장 못 부른다 — 띄우는 여기서 걸어 준다.
        CdsHelper.Game.UI.Views.ShipMapWindow.MazeGame = CdsHelper.Maze.MazeGame.Play;
        CdsHelper.Game.UI.Views.ShipMapWindow.DuelGame = CdsHelper.Duel.DuelGame.Play;

        var win = new CdsHelper.Game.UI.Views.ShipMapWindow { Owner = this };
        win.Show();
    }

    /// <summary>설정에서 켜 뒀으면 앱을 띄울 때 함대 보기도 같이 연다.</summary>
    private void OpenShipMapIfWanted()
    {
        if (!GameSettings.AutoOpenShipMap) return;
        // 본 창이 자리를 잡은 뒤에 연다 — Owner 가 아직 뜨지 않은 채로 열면 가운데가 안 맞는다.
        Dispatcher.BeginInvoke(new Action(OpenShipMap), System.Windows.Threading.DispatcherPriority.Loaded);
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
