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
    private readonly StatRerollService _rerollService = new();
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

        StartRerollCommand = new DelegateCommand(OnStartReroll, () => !IsRerolling)
            .ObservesProperty(() => IsRerolling);
        StopRerollCommand = new DelegateCommand(OnStopReroll, () => IsRerolling)
            .ObservesProperty(() => IsRerolling);
        TestReadStatCommand = new DelegateCommand(OnTestReadStat);
        LearnDigitsCommand = new DelegateCommand(OnLearnDigits);

        _autoPlayService.StatusChanged += OnStatusChanged;
        _autoPlayService.LogMessage += OnLogMessage;
        _detector.StateChanged += OnGameStateChanged;
        _detector.LogMessage += OnLogMessage;

        _rerollService.LogMessage += OnLogMessage;
        _rerollService.Progress += OnRerollProgress;
        _rerollService.Completed += OnRerollCompleted;
        _rerollService.Stopped += () => Application.Current?.Dispatcher.Invoke(() => IsRerolling = false);

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

    #region 능력치 리롤 Properties

    private bool _isRerolling;
    public bool IsRerolling
    {
        get => _isRerolling;
        set => SetProperty(ref _isRerolling, value);
    }

    // 목표치 (0=무시)
    private int _targetHp; public int TargetHp { get => _targetHp; set => SetProperty(ref _targetHp, value); }
    private int _targetInt = 76; public int TargetInt { get => _targetInt; set => SetProperty(ref _targetInt, value); }
    private int _targetStr; public int TargetStr { get => _targetStr; set => SetProperty(ref _targetStr, value); }
    private int _targetCha; public int TargetCha { get => _targetCha; set => SetProperty(ref _targetCha, value); }
    private int _targetLuck; public int TargetLuck { get => _targetLuck; set => SetProperty(ref _targetLuck, value); }
    private int _targetBonus; public int TargetBonus { get => _targetBonus; set => SetProperty(ref _targetBonus, value); }

    private int _rerollAttempts;
    public int RerollAttempts { get => _rerollAttempts; set => SetProperty(ref _rerollAttempts, value); }

    private string _rerollStatusText = "대기 중";
    public string RerollStatusText { get => _rerollStatusText; set => SetProperty(ref _rerollStatusText, value); }

    private int _clickDelay = 50;
    public int ClickDelay { get => _clickDelay; set => SetProperty(ref _clickDelay, value); }

    private int _maxAttempts = 20;
    public int MaxAttempts { get => _maxAttempts; set => SetProperty(ref _maxAttempts, value); }

    // 숫자 학습용: 5개 능력치 값 (체력,지력,무력,매력,운,보너스)
    private string _learnStatValues = "79,50,74,59,80,15";
    public string LearnStatValues
    {
        get => _learnStatValues;
        set => SetProperty(ref _learnStatValues, value);
    }

    #endregion

    #region Commands

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand CaptureTemplateCommand { get; }
    public ICommand StartRerollCommand { get; }
    public ICommand StopRerollCommand { get; }
    public ICommand TestReadStatCommand { get; }
    public ICommand LearnDigitsCommand { get; }

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

    #region 능력치 리롤 핸들러

    private void OnStartReroll()
    {
        IsRerolling = true;
        RerollAttempts = 0;
        RerollStatusText = "감지 중...";

        _rerollService.Start(
            new[] { TargetHp, TargetInt, TargetStr, TargetCha, TargetLuck, TargetBonus },
            ClickDelay, MaxAttempts);
    }

    private void OnStopReroll()
    {
        _rerollService.Stop();
        IsRerolling = false;
        RerollStatusText = "중지됨";
    }

    private void OnTestReadStat()
    {
        _rerollService.TestRead();
    }

    private void OnLearnDigits()
    {
        _rerollService.LearnDigits(LearnStatValues);
    }

    private void OnRerollProgress(int value, int attempts)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            RerollAttempts = attempts;
            RerollStatusText = $"리롤 중... ({attempts}회)";
        });
    }

    private void OnRerollCompleted(int value, int attempts)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            RerollAttempts = attempts;
            RerollStatusText = $"목표 달성! ({attempts}회)";
            IsRerolling = false;
        });
    }

    #endregion

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
