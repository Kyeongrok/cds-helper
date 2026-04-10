using System.Collections.ObjectModel;
using System.Drawing;
using System.Windows.Threading;
using CdsHelper.Support.Local.Events;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using Prism.Events;
using Prism.Ioc;
using Prism.Mvvm;

namespace CdsHelper.Main.UI.ViewModels;

public class MapContentViewModel : BindableBase
{
    private readonly CityService _cityService;
    private readonly CoordinateOcrService _coordinateOcr;
    private readonly IEventAggregator _eventAggregator;
    private List<City> _allCities = new();
    private DispatcherTimer? _trackingTimer;
    private bool _isTracking;
    private bool _isProcessing;
    private double _lastLat;
    private double _lastLon;
    private bool _hasLastCoord;
    private string? _lastGameDate;

    // 자동이동
    private CancellationTokenSource? _navCts;
    private bool _isNavigating;

    public bool IsTracking
    {
        get => _isTracking;
        set => SetProperty(ref _isTracking, value);
    }

    public bool IsNavigating
    {
        get => _isNavigating;
        set => SetProperty(ref _isNavigating, value);
    }

    public event Action<string>? NavigationStatusChanged;

    private ObservableCollection<City> _cities = new();
    public ObservableCollection<City> Cities
    {
        get => _cities;
        set => SetProperty(ref _cities, value);
    }

    public MapContentViewModel(CityService cityService)
    {
        _cityService = cityService;
        _coordinateOcr = new CoordinateOcrService();
        _eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
        Initialize();
    }

    public void StartTracking()
    {
        if (_isTracking) return;
        IsTracking = true;
        _isProcessing = false;

        _trackingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _trackingTimer.Tick += OnTrackingTimerTick;
        _trackingTimer.Start();
    }

    public void StopTracking()
    {
        IsTracking = false;
        _trackingTimer?.Stop();
        _trackingTimer = null;

        _eventAggregator.GetEvent<CurrentCoordinateEvent>().Publish(
            new CurrentCoordinateEventArgs { IsTracking = false });
    }

    private async void OnTrackingTimerTick(object? sender, EventArgs e)
    {
        if (_isProcessing) return;
        _isProcessing = true;

        try
        {
            var hWnd = GameWindowHelper.FindGameWindow();
            if (hWnd == IntPtr.Zero) return;

            var bitmap = GameWindowHelper.CaptureClient(hWnd);
            if (bitmap == null) return;

            var (prediction, dateStr) = await Task.Run(() =>
                _coordinateOcr.PredictAllAsync(bitmap));

            bitmap.Dispose();

            if (dateStr != null)
                _lastGameDate = dateStr;

            if (prediction != null)
            {
                _lastLat = prediction.ToLat();
                _lastLon = prediction.ToLon();
                _hasLastCoord = true;

                _eventAggregator.GetEvent<CurrentCoordinateEvent>().Publish(
                    new CurrentCoordinateEventArgs
                    {
                        Latitude = _lastLat,
                        Longitude = _lastLon,
                        IsTracking = true,
                        GameDate = _lastGameDate
                    });
            }
            else if (_hasLastCoord)
            {
                // 인식 실패 시 마지막 좌표를 stale로 표시
                _eventAggregator.GetEvent<CurrentCoordinateEvent>().Publish(
                    new CurrentCoordinateEventArgs
                    {
                        Latitude = _lastLat,
                        Longitude = _lastLon,
                        IsTracking = true,
                        IsStale = true,
                        GameDate = _lastGameDate
                    });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MapContentViewModel] TrackCoordinate Error: {ex.Message}");
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private void Initialize()
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var citiesPath = System.IO.Path.Combine(basePath, "cities.json");

        if (System.IO.File.Exists(citiesPath))
        {
            LoadCities(citiesPath);
        }
    }

    private void LoadCities(string filePath)
    {
        try
        {
            _allCities = _cityService.LoadCities(filePath);
            Cities = new ObservableCollection<City>(_allCities);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MapContentViewModel] LoadCities Error: {ex.Message}");
        }
    }

    /// <summary>
    /// 마커로 표시할 도시 목록 (PixelX, PixelY가 있는 도시만)
    /// DB 캐시에서 최신 데이터를 가져옴
    /// </summary>
    public IEnumerable<City> GetCitiesWithCoordinates()
    {
        // DB 캐시에서 최신 데이터 가져오기
        var cities = _cityService.GetCachedCities();
        if (cities.Count > 0)
        {
            return cities.Where(c => c.PixelX.HasValue && c.PixelY.HasValue && c.PixelX > 0 && c.PixelY > 0);
        }

        // 캐시가 없으면 메모리 데이터 사용
        return _allCities.Where(c => c.PixelX.HasValue && c.PixelY.HasValue && c.PixelX > 0 && c.PixelY > 0);
    }

    #region 자동이동

    public void StartNavigation(double destLat, double destLon)
    {
        if (_isNavigating) return;

        var hWnd = GameWindowHelper.FindGameWindow();
        if (hWnd == IntPtr.Zero)
        {
            NavigationStatusChanged?.Invoke("게임 창을 찾을 수 없습니다.");
            return;
        }

        // 추적이 꺼져 있으면 자동으로 켜기
        if (!_isTracking)
            StartTracking();

        IsNavigating = true;
        _navCts = new CancellationTokenSource();

        Task.Run(async () =>
        {
            var token = _navCts.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var gameHwnd = GameWindowHelper.FindGameWindow();
                    if (gameHwnd == IntPtr.Zero)
                    {
                        NavigationStatusChanged?.Invoke("게임 창 없음");
                        await Task.Delay(2000, token);
                        continue;
                    }

                    using var bitmap = GameWindowHelper.CaptureClient(gameHwnd);
                    if (bitmap == null)
                    {
                        await Task.Delay(500, token);
                        continue;
                    }

                    var prediction = await _coordinateOcr.PredictOcrAsync(bitmap);
                    if (prediction == null)
                    {
                        NavigationStatusChanged?.Invoke("좌표 인식 실패");
                        await Task.Delay(500, token);
                        continue;
                    }

                    var curLat = prediction.ToLat();
                    var curLon = prediction.ToLon();

                    // 좌표 추적 이벤트도 발행
                    _eventAggregator.GetEvent<CurrentCoordinateEvent>().Publish(
                        new CurrentCoordinateEventArgs
                        {
                            Latitude = curLat,
                            Longitude = curLon,
                            IsTracking = true
                        });

                    // 도착 판정
                    if (NavigationCalculator.IsNear(curLat, curLon, destLat, destLon, threshold: 2.0))
                    {
                        GameWindowHelper.SendNumpadKey(gameHwnd, 5);
                        NavigationStatusChanged?.Invoke("도착!");
                        System.Windows.Application.Current?.Dispatcher.Invoke(() => IsNavigating = false);
                        return;
                    }

                    // 방위각 → 숫자패드
                    var bearing = NavigationCalculator.BearingDegrees(curLat, curLon, destLat, destLon);
                    var numpad = GameWindowHelper.BearingToNumpad(bearing);
                    GameWindowHelper.SendNumpadKey(gameHwnd, numpad);

                    var dirLabel = numpad switch
                    {
                        8 => "N↑", 9 => "NE↗", 6 => "E→", 3 => "SE↘",
                        2 => "S↓", 1 => "SW↙", 4 => "W←", 7 => "NW↖",
                        _ => "?"
                    };
                    NavigationStatusChanged?.Invoke($"{prediction} → {dirLabel}");

                    await Task.Delay(500, token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    NavigationStatusChanged?.Invoke($"오류: {ex.Message}");
                    await Task.Delay(2000, token);
                }
            }
        });
    }

    public void StopNavigation()
    {
        _navCts?.Cancel();
        _navCts = null;
        IsNavigating = false;
        NavigationStatusChanged?.Invoke("중지됨");

        var hWnd = GameWindowHelper.FindGameWindow();
        if (hWnd != IntPtr.Zero)
            GameWindowHelper.SendNumpadKey(hWnd, 5);
    }

    #endregion
}
