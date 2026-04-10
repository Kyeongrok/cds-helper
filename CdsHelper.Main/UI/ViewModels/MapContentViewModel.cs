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

    public bool IsTracking
    {
        get => _isTracking;
        set => SetProperty(ref _isTracking, value);
    }

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

            var prediction = await Task.Run(() =>
                _coordinateOcr.PredictOcrAsync(bitmap));

            bitmap.Dispose();

            if (prediction == null) return;

            _eventAggregator.GetEvent<CurrentCoordinateEvent>().Publish(
                new CurrentCoordinateEventArgs
                {
                    Latitude = prediction.ToLat(),
                    Longitude = prediction.ToLon(),
                    IsTracking = true
                });
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
}
