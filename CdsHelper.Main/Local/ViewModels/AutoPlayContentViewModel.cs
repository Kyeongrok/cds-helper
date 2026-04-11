using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
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
        AutoLearnDigitsCommand = new DelegateCommand(OnAutoLearnDigits);
        ClearTemplatesCommand = new DelegateCommand(OnClearTemplates);

        StartCollectCoordsCommand = new DelegateCommand(OnStartCollectCoords, () => !IsCollectingCoords)
            .ObservesProperty(() => IsCollectingCoords);
        StopCollectCoordsCommand = new DelegateCommand(OnStopCollectCoords, () => IsCollectingCoords)
            .ObservesProperty(() => IsCollectingCoords);
        TrainCoordModelCommand = new DelegateCommand(OnTrainCoordModel, () => !IsTrainingCoords)
            .ObservesProperty(() => IsTrainingCoords);
        CapturePreviewCommand = new DelegateCommand(OnCapturePreview);
        OpenCoordDataFolderCommand = new DelegateCommand(OnOpenCoordDataFolder);
        AutoLabelCoordsCommand = new DelegateCommand(OnAutoLabelCoords);
        AddCoordLabelCommand = new DelegateCommand(OnAddCoordLabel);
        StartRecognizeCommand = new DelegateCommand(OnStartRecognize, () => !IsRecognizing)
            .ObservesProperty(() => IsRecognizing);
        StopRecognizeCommand = new DelegateCommand(OnStopRecognize, () => IsRecognizing)
            .ObservesProperty(() => IsRecognizing);
        StartNavCommand = new DelegateCommand(OnStartNav, () => !IsNavigating)
            .ObservesProperty(() => IsNavigating);
        StopNavCommand = new DelegateCommand(OnStopNav, () => IsNavigating)
            .ObservesProperty(() => IsNavigating);
        TestSeaMapCommand = new DelegateCommand(OnTestSeaMap);

        _autoPlayService.StatusChanged += OnStatusChanged;
        _autoPlayService.LogMessage += OnLogMessage;
        _detector.StateChanged += OnGameStateChanged;
        _detector.LogMessage += OnLogMessage;

        _rerollService.LogMessage += OnLogMessage;
        _rerollService.Progress += OnRerollProgress;
        _rerollService.Completed += OnRerollCompleted;
        _rerollService.Stopped += () => Application.Current?.Dispatcher.Invoke(() => IsRerolling = false);
        _rerollService.TemplatesChanged += () => Application.Current?.Dispatcher.Invoke(RefreshDigitImages);

        LoadCities();
        LoadCaptureScenes();
        RefreshDigitImages();
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

    // 학습된 숫자 이미지 (0~9)
    private BitmapImage?[] _digitImages = new BitmapImage?[10];
    public BitmapImage? Digit0 { get => _digitImages[0]; set => SetProperty(ref _digitImages[0], value); }
    public BitmapImage? Digit1 { get => _digitImages[1]; set => SetProperty(ref _digitImages[1], value); }
    public BitmapImage? Digit2 { get => _digitImages[2]; set => SetProperty(ref _digitImages[2], value); }
    public BitmapImage? Digit3 { get => _digitImages[3]; set => SetProperty(ref _digitImages[3], value); }
    public BitmapImage? Digit4 { get => _digitImages[4]; set => SetProperty(ref _digitImages[4], value); }
    public BitmapImage? Digit5 { get => _digitImages[5]; set => SetProperty(ref _digitImages[5], value); }
    public BitmapImage? Digit6 { get => _digitImages[6]; set => SetProperty(ref _digitImages[6], value); }
    public BitmapImage? Digit7 { get => _digitImages[7]; set => SetProperty(ref _digitImages[7], value); }
    public BitmapImage? Digit8 { get => _digitImages[8]; set => SetProperty(ref _digitImages[8], value); }
    public BitmapImage? Digit9 { get => _digitImages[9]; set => SetProperty(ref _digitImages[9], value); }

    #endregion

    #region 좌표 인식 Properties

    private bool _isCollectingCoords;
    public bool IsCollectingCoords
    {
        get => _isCollectingCoords;
        set => SetProperty(ref _isCollectingCoords, value);
    }

    private bool _isTrainingCoords;
    public bool IsTrainingCoords
    {
        get => _isTrainingCoords;
        set => SetProperty(ref _isTrainingCoords, value);
    }

    private string _coordStatusText = "대기 중";
    public string CoordStatusText
    {
        get => _coordStatusText;
        set => SetProperty(ref _coordStatusText, value);
    }

    private int _coordTrainEpochs = 50;
    public int CoordTrainEpochs
    {
        get => _coordTrainEpochs;
        set => SetProperty(ref _coordTrainEpochs, value);
    }

    // 수동 라벨링용 입력 (북위=0/남위=1, 위도값, 동경=0/서경=1, 경도값)
    private string _coordLabelInput = "0,38,1,10";
    public string CoordLabelInput
    {
        get => _coordLabelInput;
        set => SetProperty(ref _coordLabelInput, value);
    }

    private int _recognizeInterval = 500;
    public int RecognizeInterval
    {
        get => _recognizeInterval;
        set => SetProperty(ref _recognizeInterval, value);
    }

    // 목표 좌표
    private bool _targetIsNorth = true;
    public bool TargetIsNorth { get => _targetIsNorth; set => SetProperty(ref _targetIsNorth, value); }

    private int _targetLat;
    public int TargetLat { get => _targetLat; set => SetProperty(ref _targetLat, value); }

    private bool _targetIsEast = true;
    public bool TargetIsEast { get => _targetIsEast; set => SetProperty(ref _targetIsEast, value); }

    private int _targetLon;
    public int TargetLon { get => _targetLon; set => SetProperty(ref _targetLon, value); }

    private bool _isNavigating;
    public bool IsNavigating
    {
        get => _isNavigating;
        set => SetProperty(ref _isNavigating, value);
    }

    private string _navStatusText = "-";
    public string NavStatusText
    {
        get => _navStatusText;
        set => SetProperty(ref _navStatusText, value);
    }

    private bool _isRecognizing;
    public bool IsRecognizing
    {
        get => _isRecognizing;
        set => SetProperty(ref _isRecognizing, value);
    }

    private string _recognizedCoordText = "-";
    public string RecognizedCoordText
    {
        get => _recognizedCoordText;
        set => SetProperty(ref _recognizedCoordText, value);
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
    public ICommand AutoLearnDigitsCommand { get; }
    public ICommand ClearTemplatesCommand { get; }

    // 좌표 인식 커맨드
    public ICommand StartCollectCoordsCommand { get; }
    public ICommand StopCollectCoordsCommand { get; }
    public ICommand TrainCoordModelCommand { get; }
    public ICommand CapturePreviewCommand { get; }
    public ICommand OpenCoordDataFolderCommand { get; }
    public ICommand AutoLabelCoordsCommand { get; }
    public ICommand AddCoordLabelCommand { get; }
    public ICommand StartRecognizeCommand { get; }
    public ICommand StopRecognizeCommand { get; }
    public ICommand StartNavCommand { get; }
    public ICommand StopNavCommand { get; }
    public ICommand TestSeaMapCommand { get; }

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
        AddLog("Ctrl + Alt 키를 동시에 누르면 자동 플레이를 중지할 수 있습니다.");

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
        RefreshDigitImages();
    }

    private void OnAutoLearnDigits()
    {
        _rerollService.AutoLearnDigits();
        RefreshDigitImages();
    }

    private void OnClearTemplates()
    {
        _rerollService.ClearTemplates();
        RefreshDigitImages();
    }

    private void RefreshDigitImages()
    {
        var dir = _rerollService.NumberTemplateDir;
        for (int d = 0; d <= 9; d++)
        {
            var path = Path.Combine(dir, $"{d}.png");
            BitmapImage? img = null;
            if (File.Exists(path))
            {
                img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.UriSource = new Uri(path, UriKind.Absolute);
                img.EndInit();
                img.Freeze();
            }
            switch (d)
            {
                case 0: Digit0 = img; break;
                case 1: Digit1 = img; break;
                case 2: Digit2 = img; break;
                case 3: Digit3 = img; break;
                case 4: Digit4 = img; break;
                case 5: Digit5 = img; break;
                case 6: Digit6 = img; break;
                case 7: Digit7 = img; break;
                case 8: Digit8 = img; break;
                case 9: Digit9 = img; break;
            }
        }
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

    #region 좌표 인식 핸들러

    private CancellationTokenSource? _recognizeCts;

    private void OnStartRecognize()
    {
        IsRecognizing = true;
        RecognizedCoordText = "인식 중...";
        _recognizeCts = new CancellationTokenSource();

        Task.Run(async () =>
        {
            var token = _recognizeCts.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var hWnd = GameWindowHelper.FindGameWindow();
                    if (hWnd == IntPtr.Zero)
                    {
                        UpdateRecognizedText("게임 윈도우 없음");
                        await Task.Delay(2000, token);
                        continue;
                    }

                    using var bitmap = GameWindowHelper.CaptureClient(hWnd);
                    if (bitmap == null)
                    {
                        await Task.Delay(RecognizeInterval, token);
                        continue;
                    }

                    var prediction = await _autoPlayService.CoordinateOcr.PredictOcrAsync(bitmap);
                    if (prediction != null)
                        UpdateRecognizedText(prediction.ToString());
                    else
                        UpdateRecognizedText("인식 실패");

                    await Task.Delay(RecognizeInterval, token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    UpdateRecognizedText($"오류: {ex.Message}");
                    await Task.Delay(2000, token);
                }
            }
        });
    }

    private void OnStopRecognize()
    {
        _recognizeCts?.Cancel();
        IsRecognizing = false;
        RecognizedCoordText = "중지됨";
    }

    private CancellationTokenSource? _navCts;

    private void OnStartNav()
    {
        IsNavigating = true;
        NavStatusText = "항해 시작...";
        _navCts = new CancellationTokenSource();

        var destLat = TargetIsNorth ? TargetLat : -TargetLat;
        var destLon = TargetIsEast ? TargetLon : -TargetLon;

        Task.Run(async () =>
        {
            var token = _navCts.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var hWnd = GameWindowHelper.FindGameWindow();
                    if (hWnd == IntPtr.Zero)
                    {
                        UpdateNavStatus("게임 윈도우 없음");
                        await Task.Delay(2000, token);
                        continue;
                    }

                    using var bitmap = GameWindowHelper.CaptureClient(hWnd);
                    if (bitmap == null)
                    {
                        await Task.Delay(RecognizeInterval, token);
                        continue;
                    }

                    var prediction = await _autoPlayService.CoordinateOcr.PredictOcrAsync(bitmap);
                    if (prediction == null)
                    {
                        UpdateNavStatus("좌표 인식 실패");
                        await Task.Delay(RecognizeInterval, token);
                        continue;
                    }

                    var curLat = prediction.ToLat();
                    var curLon = prediction.ToLon();

                    // 도착 판정
                    if (NavigationCalculator.IsNear(curLat, curLon, destLat, destLon, threshold: 2.0))
                    {
                        GameWindowHelper.SendNumpadKey(hWnd, 5); // 정지
                        UpdateNavStatus($"도착! {prediction}");
                        UpdateRecognizedText(prediction.ToString());
                        Application.Current?.Dispatcher.Invoke(() => IsNavigating = false);
                        AddLog($"목적지 도착: {prediction}");
                        return;
                    }

                    // 방위각 → 숫자패드
                    var bearing = NavigationCalculator.BearingDegrees(curLat, curLon, destLat, destLon);
                    var numpad = GameWindowHelper.BearingToNumpad(bearing);

                    GameWindowHelper.SendNumpadKey(hWnd, numpad);

                    var dirLabel = numpad switch
                    {
                        8 => "N↑", 9 => "NE↗", 6 => "E→", 3 => "SE↘",
                        2 => "S↓", 1 => "SW↙", 4 => "W←", 7 => "NW↖",
                        _ => "?"
                    };

                    UpdateNavStatus($"{prediction} → {dirLabel} (방위 {bearing:F0}°, 키:{numpad})");
                    UpdateRecognizedText(prediction.ToString());

                    await Task.Delay(RecognizeInterval, token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    UpdateNavStatus($"오류: {ex.Message}");
                    await Task.Delay(2000, token);
                }
            }
        });
    }

    private void OnStopNav()
    {
        _navCts?.Cancel();
        IsNavigating = false;
        NavStatusText = "중지됨";

        // 정지 키 전송
        var hWnd = GameWindowHelper.FindGameWindow();
        if (hWnd != IntPtr.Zero)
            GameWindowHelper.SendNumpadKey(hWnd, 5);
    }

    private void UpdateNavStatus(string text)
    {
        Application.Current?.Dispatcher.Invoke(() => NavStatusText = text);
    }

    private void OnTestSeaMap()
    {
        var hWnd = GameWindowHelper.FindGameWindow();
        if (hWnd == IntPtr.Zero)
        {
            AddLog("게임 윈도우를 찾을 수 없습니다.");
            return;
        }

        GameWindowHelper.BringToFront(hWnd);
        Thread.Sleep(300);

        using var bitmap = GameWindowHelper.CaptureClient(hWnd);
        if (bitmap == null) return;

        var dataDir = _autoPlayService.CoordinateOcr.DataDirectory;
        var debugPath = Path.Combine(dataDir, "seamap_debug.png");
        _autoPlayService.SeaMap.SaveDebugImage(bitmap, debugPath);

        var grid = _autoPlayService.SeaMap.Analyze(bitmap);
        var shipFound = false;
        for (var r = 0; r < grid.GetLength(0) && !shipFound; r++)
        for (var c = 0; c < grid.GetLength(1) && !shipFound; c++)
            if (grid[r, c] == 2) shipFound = true;

        AddLog($"해도 분석 완료: {debugPath}");
        AddLog($"  배 감지: {(shipFound ? "성공" : "실패 — ship 템플릿 필요")}");
        System.Diagnostics.Process.Start("explorer.exe", debugPath);
    }

    private void UpdateRecognizedText(string text)
    {
        Application.Current?.Dispatcher.Invoke(() => RecognizedCoordText = text);
    }

    private void OnStartCollectCoords()
    {
        IsCollectingCoords = true;
        CoordStatusText = "데이터 수집 중...";
        _autoPlayService.CoordinateOcr.StartCollecting();
    }

    private void OnStopCollectCoords()
    {
        _autoPlayService.CoordinateOcr.StopCollecting();
        IsCollectingCoords = false;
        var count = _autoPlayService.CoordinateOcr.GetCollectedCount();
        CoordStatusText = $"수집 완료 ({count}장)";
    }

    private void OnTrainCoordModel()
    {
        CoordStatusText = "학습 기능은 별도 프로젝트로 분리되었습니다.";
    }

    private void OnCapturePreview()
    {
        var path = _autoPlayService.CoordinateOcr.CapturePreview();
        if (path != null)
            AddLog($"스크린샷 저장: {path}");
        else
            AddLog("캡처 실패 — 게임이 실행 중인지 확인하세요.");
    }

    private void OnAutoLabelCoords()
    {
        CoordStatusText = "자동 라벨링 중 (Windows OCR)...";

        Task.Run(async () =>
        {
            var count = await _autoPlayService.CoordinateOcr.AutoLabelAsync();
            var total = _autoPlayService.CoordinateOcr.GetLabeledCount();
            Application.Current?.Dispatcher.Invoke(() =>
            {
                CoordStatusText = $"자동 라벨링 완료: {count}장 (총 {total}장)";
            });
        });
    }

    private void OnOpenCoordDataFolder()
    {
        var folder = _autoPlayService.CoordinateOcr.DataDirectory;
        Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start("explorer.exe", folder);
    }

    private void OnAddCoordLabel()
    {
        // 가장 최근 수집 이미지에 라벨 추가
        var parts = CoordLabelInput.Split(',');
        if (parts.Length != 4)
        {
            AddLog("라벨 형식 오류: latDir,latVal,lonDir,lonVal (예: 0,38,1,10)");
            return;
        }

        var imageDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "git", "ml", "cds-ai", "assets", "coordinate_data", "images");

        if (!Directory.Exists(imageDir)) return;

        var latest = Directory.GetFiles(imageDir, "*.png")
            .OrderByDescending(f => f)
            .FirstOrDefault();

        if (latest == null)
        {
            AddLog("라벨링할 이미지가 없습니다.");
            return;
        }

        _autoPlayService.CoordinateOcr.AddLabel(
            Path.GetFileName(latest),
            int.Parse(parts[0]), int.Parse(parts[1]),
            int.Parse(parts[2]), int.Parse(parts[3]));
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
