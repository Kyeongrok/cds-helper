using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using Prism.Commands;
using Prism.Ioc;
using Prism.Mvvm;

namespace CdsHelper.Main.Local.ViewModels;

public class AutoPlayContentViewModel : BindableBase
{
    private readonly AutoPlayService _autoPlayService = new();
    private readonly CityService _cityService;
    private readonly GameStateDetector _detector;

    public AutoPlayContentViewModel()
    {
        _cityService = ContainerLocator.Container.Resolve<CityService>();
        _detector = new GameStateDetector();

        StartCommand = new DelegateCommand(OnStart, () => !IsRunning)
            .ObservesProperty(() => IsRunning);

        StopCommand = new DelegateCommand(OnStop, () => IsRunning)
            .ObservesProperty(() => IsRunning);

        CaptureTemplateCommand = new DelegateCommand(OnCaptureTemplate, () => IsRunning && SelectedCaptureScene != null)
            .ObservesProperty(() => IsRunning)
            .ObservesProperty(() => SelectedCaptureScene);

        _autoPlayService.StatusChanged += OnStatusChanged;
        _autoPlayService.LogMessage += OnLogMessage;
        _detector.StateChanged += OnGameStateChanged;
        _detector.LogMessage += OnLogMessage;

        LoadCities();
        LoadCaptureScenes();
    }

    #region Game Detection Properties

    private bool _isGameRunning;
    public bool IsGameRunning
    {
        get => _isGameRunning;
        set => SetProperty(ref _isGameRunning, value);
    }

    private string _gameRunningDisplay = "-";
    public string GameRunningDisplay
    {
        get => _gameRunningDisplay;
        set => SetProperty(ref _gameRunningDisplay, value);
    }

    private string _gameSceneDisplay = "-";
    public string GameSceneDisplay
    {
        get => _gameSceneDisplay;
        set => SetProperty(ref _gameSceneDisplay, value);
    }

    private string _gameSceneIcon = "⏹️";
    public string GameSceneIcon
    {
        get => _gameSceneIcon;
        set => SetProperty(ref _gameSceneIcon, value);
    }

    #endregion

    #region Capture Template Properties

    private ObservableCollection<CaptureSceneItem> _captureScenes = new();
    public ObservableCollection<CaptureSceneItem> CaptureScenes
    {
        get => _captureScenes;
        set => SetProperty(ref _captureScenes, value);
    }

    private CaptureSceneItem? _selectedCaptureScene;
    public CaptureSceneItem? SelectedCaptureScene
    {
        get => _selectedCaptureScene;
        set => SetProperty(ref _selectedCaptureScene, value);
    }

    #endregion

    #region AutoPlay Properties

    private ObservableCollection<City> _cities = new();
    public ObservableCollection<City> Cities
    {
        get => _cities;
        set => SetProperty(ref _cities, value);
    }

    private City? _selectedCity;
    public City? SelectedCity
    {
        get => _selectedCity;
        set => SetProperty(ref _selectedCity, value);
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value);
    }

    private string _statusText = "대기 중";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private string _currentLatDisplay = "-";
    public string CurrentLatDisplay
    {
        get => _currentLatDisplay;
        set => SetProperty(ref _currentLatDisplay, value);
    }

    private string _currentLonDisplay = "-";
    public string CurrentLonDisplay
    {
        get => _currentLonDisplay;
        set => SetProperty(ref _currentLonDisplay, value);
    }

    private string _bearingDisplay = "-";
    public string BearingDisplay
    {
        get => _bearingDisplay;
        set => SetProperty(ref _bearingDisplay, value);
    }

    private string _destinationDisplay = "-";
    public string DestinationDisplay
    {
        get => _destinationDisplay;
        set => SetProperty(ref _destinationDisplay, value);
    }

    private ObservableCollection<string> _logMessages = new();
    public ObservableCollection<string> LogMessages
    {
        get => _logMessages;
        set => SetProperty(ref _logMessages, value);
    }

    #endregion

    #region Commands

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand CaptureTemplateCommand { get; }

    #endregion

    private void LoadCities()
    {
        var cities = _cityService.GetCachedCities();
        Cities = new ObservableCollection<City>(
            cities.Where(c => c.Latitude.HasValue && c.Longitude.HasValue)
                  .OrderBy(c => c.Name));
    }

    private void LoadCaptureScenes()
    {
        CaptureScenes = new ObservableCollection<CaptureSceneItem>
        {
            new(GameScene.City, "도시 전경"),
            new(GameScene.PortMenu, "항구 메뉴"),
            new(GameScene.Trade, "교역소"),
            new(GameScene.Inn, "여관"),
            new(GameScene.Guild, "길드"),
            new(GameScene.Shipyard, "조선소"),
            new(GameScene.Library, "도서관"),
            new(GameScene.Palace, "왕궁"),
            new(GameScene.Supply, "보급소"),
            new(GameScene.SeaAnchored, "해상 (정박)"),
            new(GameScene.SeaNavigation, "해상 (항해 중)"),
            new(GameScene.Combat, "전투"),
            new(GameScene.Dialog, "대화창"),
        };
    }

    private void OnStart()
    {
        IsRunning = true;
        StatusText = "감지 시작...";

        _detector.Start(2000);

        if (SelectedCity != null)
        {
            DestinationDisplay = $"{SelectedCity.Name} ({SelectedCity.LatitudeDisplay}, {SelectedCity.LongitudeDisplay})";
            _autoPlayService.Start(SelectedCity, _detector);
        }
    }

    private void OnStop()
    {
        _detector.Stop();
        _autoPlayService.Stop();
        IsRunning = false;
        StatusText = "대기 중";

        IsGameRunning = false;
        GameRunningDisplay = "-";
        GameSceneDisplay = "-";
        GameSceneIcon = "⏹️";
    }

    private void OnCaptureTemplate()
    {
        if (SelectedCaptureScene == null) return;

        var path = _detector.CaptureTemplate(SelectedCaptureScene.Scene);
        if (path != null)
            AddLog($"템플릿 저장 완료: {SelectedCaptureScene.DisplayName} → {path}");
        else
            AddLog($"템플릿 캡처 실패: {SelectedCaptureScene.DisplayName}");
    }

    private void OnGameStateChanged(GameDetectionResult result)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            IsGameRunning = result.IsGameRunning;
            GameRunningDisplay = result.IsGameRunning ? "실행 중" : "실행되지 않음";
            GameSceneDisplay = result.SceneDetail;

            GameSceneIcon = result.Scene switch
            {
                GameScene.City => "🏙️",
                GameScene.PortMenu => "⚓",
                GameScene.Trade => "🏪",
                GameScene.Inn => "🏨",
                GameScene.Guild => "🏛️",
                GameScene.Shipyard => "🚢",
                GameScene.Library => "📚",
                GameScene.Palace => "👑",
                GameScene.Supply => "📦",
                GameScene.SeaAnchored => "⚓",
                GameScene.SeaNavigation => "🌊",
                GameScene.Combat => "⚔️",
                GameScene.Dialog => "💬",
                _ => result.IsGameRunning ? "❓" : "⛔"
            };
        });
    }

    private void OnStatusChanged(AutoPlayStatus status)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            StatusText = status.Message;

            if (status.CurrentLat != 0 || status.CurrentLon != 0)
            {
                CurrentLatDisplay = status.CurrentLat.ToString("F1");
                CurrentLonDisplay = status.CurrentLon.ToString("F1");
            }

            if (status.Bearing != 0)
                BearingDisplay = $"{status.Bearing:F1}°";

            if (status.State is AutoPlayState.Arrived or AutoPlayState.Stopped or AutoPlayState.Error)
                IsRunning = false;
        });
    }

    private void OnLogMessage(string message)
    {
        Application.Current?.Dispatcher.Invoke(() => AddLog(message));
    }

    private void AddLog(string message)
    {
        if (!message.StartsWith('['))
            message = $"[{DateTime.Now:HH:mm:ss}] {message}";

        LogMessages.Insert(0, message);
        while (LogMessages.Count > 200)
            LogMessages.RemoveAt(LogMessages.Count - 1);
    }
}

/// <summary>캡처 장면 선택용 항목.</summary>
public class CaptureSceneItem
{
    public GameScene Scene { get; }
    public string DisplayName { get; }

    public CaptureSceneItem(GameScene scene, string displayName)
    {
        Scene = scene;
        DisplayName = displayName;
    }

    public override string ToString() => DisplayName;
}
