using System.Collections.ObjectModel;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using Prism.Mvvm;

namespace CdsHelper.Main.UI.ViewModels;

public class MapContentViewModel : BindableBase
{
    private readonly CityService _cityService;
    private List<City> _allCities = new();

    private ObservableCollection<City> _cities = new();
    public ObservableCollection<City> Cities
    {
        get => _cities;
        set => SetProperty(ref _cities, value);
    }

    public MapContentViewModel(CityService cityService)
    {
        _cityService = cityService;
        Initialize();
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
    /// </summary>
    public IEnumerable<City> GetCitiesWithCoordinates()
    {
        return _allCities.Where(c => c.PixelX.HasValue && c.PixelY.HasValue && c.PixelX > 0 && c.PixelY > 0);
    }
}
