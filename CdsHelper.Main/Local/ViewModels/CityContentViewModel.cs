using System.Collections.ObjectModel;
using System.Windows.Input;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.UI.Views;
using Prism.Commands;
using Prism.Mvvm;

namespace CdsHelper.Main.Local.ViewModels;

public class CityContentViewModel : BindableBase
{
    private readonly CityService _cityService;

    private List<City> _allCities = new();

    #region Collections

    private ObservableCollection<City> _cities = new();
    public ObservableCollection<City> Cities
    {
        get => _cities;
        set => SetProperty(ref _cities, value);
    }

    #endregion

    #region Filter Properties

    private string _cityNameSearch = "";
    public string CityNameSearch
    {
        get => _cityNameSearch;
        set { SetProperty(ref _cityNameSearch, value); ApplyFilter(); }
    }

    private string? _selectedCulturalSphere;
    public string? SelectedCulturalSphere
    {
        get => _selectedCulturalSphere;
        set { SetProperty(ref _selectedCulturalSphere, value); ApplyFilter(); }
    }

    private bool _libraryOnly;
    public bool LibraryOnly
    {
        get => _libraryOnly;
        set { SetProperty(ref _libraryOnly, value); ApplyFilter(); }
    }

    private bool _shipyardOnly;
    public bool ShipyardOnly
    {
        get => _shipyardOnly;
        set { SetProperty(ref _shipyardOnly, value); ApplyFilter(); }
    }

    public ObservableCollection<string> CulturalSpheres { get; } = new();

    private City? _selectedCity;
    public City? SelectedCity
    {
        get => _selectedCity;
        set => SetProperty(ref _selectedCity, value);
    }

    private bool _groupByCulturalSphere;
    public bool GroupByCulturalSphere
    {
        get => _groupByCulturalSphere;
        set { SetProperty(ref _groupByCulturalSphere, value); UpdateGrouping(); }
    }

    private bool _groupByLibrary;
    public bool GroupByLibrary
    {
        get => _groupByLibrary;
        set { SetProperty(ref _groupByLibrary, value); UpdateGrouping(); }
    }

    private string? _cityGroupPropertyNames;
    public string? CityGroupPropertyNames
    {
        get => _cityGroupPropertyNames;
        private set => SetProperty(ref _cityGroupPropertyNames, value);
    }

    private bool _isCityGroupingEnabled;
    public bool IsCityGroupingEnabled
    {
        get => _isCityGroupingEnabled;
        private set => SetProperty(ref _isCityGroupingEnabled, value);
    }

    #endregion

    #region Status Properties

    private string _statusText = "준비됨";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    #endregion

    #region Commands

    public ICommand ResetFilterCommand { get; }
    public ICommand EditCityPixelCommand { get; }
    public ICommand ExportCitiesToJsonCommand { get; }

    #endregion

    public CityContentViewModel(CityService cityService)
    {
        _cityService = cityService;

        ResetFilterCommand = new DelegateCommand(ResetFilter);
        EditCityPixelCommand = new DelegateCommand<City>(EditCityPixel);
        ExportCitiesToJsonCommand = new DelegateCommand(ExportCitiesToJson);

        Initialize();
    }

    private async void Initialize()
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var dbPath = System.IO.Path.Combine(basePath, "cdshelper.db");
        var citiesPath = System.IO.Path.Combine(basePath, "cities.json");

        await _cityService.InitializeAsync(dbPath, citiesPath);
        await LoadCitiesFromDbAsync();
    }

    private async Task LoadCitiesFromDbAsync()
    {
        try
        {
            _allCities = await _cityService.LoadCitiesFromDbAsync();

            CulturalSpheres.Clear();
            CulturalSpheres.Add("전체");
            foreach (var cs in _cityService.GetDistinctCulturalSpheres(_allCities))
                CulturalSpheres.Add(cs);
            SelectedCulturalSphere = "전체";

            ApplyFilter();
            StatusText = $"도시 로드 완료: {_allCities.Count}개";
        }
        catch (Exception ex)
        {
            StatusText = "도시 로드 실패";
            System.Windows.MessageBox.Show($"도시 로드 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void ApplyFilter()
    {
        if (_allCities.Count == 0) return;

        var filtered = _cityService.Filter(
            _allCities,
            string.IsNullOrWhiteSpace(CityNameSearch) ? null : CityNameSearch,
            SelectedCulturalSphere == "전체" ? null : SelectedCulturalSphere,
            LibraryOnly,
            ShipyardOnly);

        Cities = new ObservableCollection<City>(filtered);
        StatusText = $"도시: {filtered.Count}개";
    }

    private void UpdateGrouping()
    {
        var props = new List<string>();
        if (GroupByCulturalSphere) props.Add("CulturalSphere");
        if (GroupByLibrary) props.Add("HasLibraryDisplay");

        CityGroupPropertyNames = props.Count > 0 ? string.Join(",", props) : null;
        IsCityGroupingEnabled = props.Count > 0;
    }

    private void ResetFilter()
    {
        CityNameSearch = "";
        SelectedCulturalSphere = "전체";
        LibraryOnly = false;
        ShipyardOnly = false;
    }

    private async void EditCityPixel(City? city)
    {
        if (city == null) return;

        var dialog = new EditCityPixelDialog(
            city.Name,
            city.PixelX,
            city.PixelY,
            city.HasLibrary,
            city.Latitude,
            city.Longitude,
            city.CulturalSphere)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true)
        {
            city.Name = dialog.CityName;
            city.PixelX = dialog.PixelX;
            city.PixelY = dialog.PixelY;
            city.HasLibrary = dialog.HasLibrary;
            city.Latitude = dialog.Latitude;
            city.Longitude = dialog.Longitude;
            city.CulturalSphere = dialog.CulturalSphere;

            try
            {
                await _cityService.UpdateCityAsync(city);
                StatusText = $"도시 '{city.Name}' 수정 완료";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"도시 정보 수정 실패: {ex.Message}", "오류",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }

    private async void ExportCitiesToJson()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON 파일 (*.json)|*.json",
            Title = "도시 정보 내보내기",
            FileName = "cities.json"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            await _cityService.ExportToJsonAsync(dialog.FileName);
            StatusText = $"도시 정보 내보내기 완료: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"내보내기 실패: {ex.Message}", "오류",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }
}
