using System.Collections.ObjectModel;
using System.Windows.Input;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using Prism.Commands;
using Prism.Mvvm;

namespace CdsHelper.Form.Local.ViewModels;

public class CdsHelperViewModel : BindableBase
{
    private readonly CharacterService _characterService;
    private readonly BookService _bookService;
    private readonly CityService _cityService;
    private readonly PatronService _patronService;
    private readonly FigureheadService _figureheadService;
    private readonly ItemService _itemService;
    private readonly SaveDataService _saveDataService;

    // Raw data
    private List<CharacterData> _allCharacters = new();
    private List<Book> _allBooks = new();
    private List<City> _allCities = new();
    private List<Patron> _allPatrons = new();
    private List<Figurehead> _allFigureheads = new();
    private List<Item> _allItems = new();
    private SaveGameInfo? _saveGameInfo;

    #region Collections for DataGrid

    private ObservableCollection<CharacterData> _characters = new();
    public ObservableCollection<CharacterData> Characters
    {
        get => _characters;
        set => SetProperty(ref _characters, value);
    }

    private ObservableCollection<Book> _books = new();
    public ObservableCollection<Book> Books
    {
        get => _books;
        set => SetProperty(ref _books, value);
    }

    private ObservableCollection<City> _cities = new();
    public ObservableCollection<City> Cities
    {
        get => _cities;
        set => SetProperty(ref _cities, value);
    }

    private ObservableCollection<PatronDisplay> _patrons = new();
    public ObservableCollection<PatronDisplay> Patrons
    {
        get => _patrons;
        set => SetProperty(ref _patrons, value);
    }

    private ObservableCollection<Figurehead> _figureheads = new();
    public ObservableCollection<Figurehead> Figureheads
    {
        get => _figureheads;
        set => SetProperty(ref _figureheads, value);
    }

    private ObservableCollection<Item> _items = new();
    public ObservableCollection<Item> Items
    {
        get => _items;
        set => SetProperty(ref _items, value);
    }

    #endregion

    #region Character Filter Properties

    private bool _showGrayCharacters;
    public bool ShowGrayCharacters
    {
        get => _showGrayCharacters;
        set { SetProperty(ref _showGrayCharacters, value); ApplyCharacterFilter(); }
    }

    private string _characterNameSearch = "";
    public string CharacterNameSearch
    {
        get => _characterNameSearch;
        set { SetProperty(ref _characterNameSearch, value); ApplyCharacterFilter(); }
    }

    private bool _filterBySkill;
    public bool FilterBySkill
    {
        get => _filterBySkill;
        set { SetProperty(ref _filterBySkill, value); ApplyCharacterFilter(); }
    }

    private int _selectedSkillIndex = 0;
    public int SelectedSkillIndex
    {
        get => _selectedSkillIndex;
        set { SetProperty(ref _selectedSkillIndex, value); if (FilterBySkill) ApplyCharacterFilter(); }
    }

    private byte _selectedSkillLevel = 3;
    public byte SelectedSkillLevel
    {
        get => _selectedSkillLevel;
        set { SetProperty(ref _selectedSkillLevel, value); if (FilterBySkill) ApplyCharacterFilter(); }
    }

    public List<string> SkillNames { get; } = CharacterService.SkillNames.Values.ToList();
    public List<byte> SkillLevels { get; } = new() { 1, 2, 3, 4, 5 };

    #endregion

    #region Book Filter Properties

    private string _bookNameSearch = "";
    public string BookNameSearch
    {
        get => _bookNameSearch;
        set { SetProperty(ref _bookNameSearch, value); ApplyBookFilter(); }
    }

    private string _librarySearch = "";
    public string LibrarySearch
    {
        get => _librarySearch;
        set { SetProperty(ref _librarySearch, value); ApplyBookFilter(); }
    }

    private string _hintSearch = "";
    public string HintSearch
    {
        get => _hintSearch;
        set { SetProperty(ref _hintSearch, value); ApplyBookFilter(); }
    }

    private string? _selectedLanguage;
    public string? SelectedLanguage
    {
        get => _selectedLanguage;
        set { SetProperty(ref _selectedLanguage, value); ApplyBookFilter(); }
    }

    private string? _selectedRequiredSkill;
    public string? SelectedRequiredSkill
    {
        get => _selectedRequiredSkill;
        set { SetProperty(ref _selectedRequiredSkill, value); ApplyBookFilter(); }
    }

    public ObservableCollection<string> Languages { get; } = new();
    public ObservableCollection<string> RequiredSkills { get; } = new();

    #endregion

    #region City Filter Properties

    private string _cityNameSearch = "";
    public string CityNameSearch
    {
        get => _cityNameSearch;
        set { SetProperty(ref _cityNameSearch, value); ApplyCityFilter(); }
    }

    private string? _selectedCulturalSphere;
    public string? SelectedCulturalSphere
    {
        get => _selectedCulturalSphere;
        set { SetProperty(ref _selectedCulturalSphere, value); ApplyCityFilter(); }
    }

    private bool _libraryOnly;
    public bool LibraryOnly
    {
        get => _libraryOnly;
        set { SetProperty(ref _libraryOnly, value); ApplyCityFilter(); }
    }

    private bool _shipyardOnly;
    public bool ShipyardOnly
    {
        get => _shipyardOnly;
        set { SetProperty(ref _shipyardOnly, value); ApplyCityFilter(); }
    }

    public ObservableCollection<string> CulturalSpheres { get; } = new();

    private City? _selectedCity;
    public City? SelectedCity
    {
        get => _selectedCity;
        set => SetProperty(ref _selectedCity, value);
    }

    #endregion

    #region Patron Filter Properties

    private string _patronNameSearch = "";
    public string PatronNameSearch
    {
        get => _patronNameSearch;
        set { SetProperty(ref _patronNameSearch, value); ApplyPatronFilter(); }
    }

    private string _patronCitySearch = "";
    public string PatronCitySearch
    {
        get => _patronCitySearch;
        set { SetProperty(ref _patronCitySearch, value); ApplyPatronFilter(); }
    }

    private string? _selectedNationality;
    public string? SelectedNationality
    {
        get => _selectedNationality;
        set { SetProperty(ref _selectedNationality, value); ApplyPatronFilter(); }
    }

    private bool _activePatronsOnly;
    public bool ActivePatronsOnly
    {
        get => _activePatronsOnly;
        set { SetProperty(ref _activePatronsOnly, value); ApplyPatronFilter(); }
    }

    public ObservableCollection<string> Nationalities { get; } = new();

    #endregion

    #region Figurehead Filter Properties

    private string _figureheadNameSearch = "";
    public string FigureheadNameSearch
    {
        get => _figureheadNameSearch;
        set { SetProperty(ref _figureheadNameSearch, value); ApplyFigureheadFilter(); }
    }

    private string? _selectedFigureheadFunction;
    public string? SelectedFigureheadFunction
    {
        get => _selectedFigureheadFunction;
        set { SetProperty(ref _selectedFigureheadFunction, value); ApplyFigureheadFilter(); }
    }

    private string? _selectedFigureheadLevel;
    public string? SelectedFigureheadLevel
    {
        get => _selectedFigureheadLevel;
        set { SetProperty(ref _selectedFigureheadLevel, value); ApplyFigureheadFilter(); }
    }

    public ObservableCollection<string> FigureheadFunctions { get; } = new();
    public ObservableCollection<string> FigureheadLevels { get; } = new();

    #endregion

    #region Item Filter Properties

    private string _itemNameSearch = "";
    public string ItemNameSearch
    {
        get => _itemNameSearch;
        set { SetProperty(ref _itemNameSearch, value); ApplyItemFilter(); }
    }

    private string? _selectedItemCategory;
    public string? SelectedItemCategory
    {
        get => _selectedItemCategory;
        set { SetProperty(ref _selectedItemCategory, value); ApplyItemFilter(); }
    }

    private string _itemDiscoverySearch = "";
    public string ItemDiscoverySearch
    {
        get => _itemDiscoverySearch;
        set { SetProperty(ref _itemDiscoverySearch, value); ApplyItemFilter(); }
    }

    public ObservableCollection<string> ItemCategories { get; } = new();

    #endregion

    #region Status Properties

    private string _statusText = "준비됨";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private string _filePath = "파일 경로: 없음";
    public string FilePath
    {
        get => _filePath;
        set => SetProperty(ref _filePath, value);
    }

    private string _windowTitle = "대항해시대3 세이브 뷰어";
    public string WindowTitle
    {
        get => _windowTitle;
        set => SetProperty(ref _windowTitle, value);
    }

    public int CurrentYear => _saveGameInfo?.Year ?? 1480;

    #endregion

    #region Commands

    public ICommand LoadSaveCommand { get; }
    public ICommand ResetBookFilterCommand { get; }
    public ICommand ResetCityFilterCommand { get; }
    public ICommand ResetPatronFilterCommand { get; }
    public ICommand ResetFigureheadFilterCommand { get; }
    public ICommand ResetItemFilterCommand { get; }
    public ICommand EditCityPixelCommand { get; }
    public ICommand ExportCitiesToJsonCommand { get; }

    #endregion

    public CdsHelperViewModel(
        CharacterService characterService,
        BookService bookService,
        CityService cityService,
        PatronService patronService,
        FigureheadService figureheadService,
        ItemService itemService,
        SaveDataService saveDataService)
    {
        _characterService = characterService;
        _bookService = bookService;
        _cityService = cityService;
        _patronService = patronService;
        _figureheadService = figureheadService;
        _itemService = itemService;
        _saveDataService = saveDataService;

        LoadSaveCommand = new DelegateCommand(LoadSaveFile);
        ResetBookFilterCommand = new DelegateCommand(ResetBookFilter);
        ResetCityFilterCommand = new DelegateCommand(ResetCityFilter);
        ResetPatronFilterCommand = new DelegateCommand(ResetPatronFilter);
        ResetFigureheadFilterCommand = new DelegateCommand(ResetFigureheadFilter);
        ResetItemFilterCommand = new DelegateCommand(ResetItemFilter);
        EditCityPixelCommand = new DelegateCommand<City>(EditCityPixel);
        ExportCitiesToJsonCommand = new DelegateCommand(ExportCitiesToJson);

        // 앱 시작 시 데이터 로드
        Initialize();
    }

    private async void Initialize()
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var dbPath = System.IO.Path.Combine(basePath, "cdshelper.db");

        var booksPath = System.IO.Path.Combine(basePath, "books.json");
        var citiesPath = System.IO.Path.Combine(basePath, "cities.json");
        var patronsPath = System.IO.Path.Combine(basePath, "patrons.json");
        var figureheadsPath = System.IO.Path.Combine(basePath, "figurehead.json");
        var itemsPath = System.IO.Path.Combine(basePath, "item.json");

        // DB 초기화 및 도시/도서 로드 (DB 우선, JSON은 마이그레이션용)
        await _cityService.InitializeAsync(dbPath, citiesPath);
        await LoadCitiesFromDbAsync();

        await _bookService.InitializeAsync(dbPath, booksPath);
        await LoadBooksFromDbAsync();

        if (System.IO.File.Exists(patronsPath))
            LoadPatrons(patronsPath);

        if (System.IO.File.Exists(figureheadsPath))
            LoadFigureheads(figureheadsPath);

        if (System.IO.File.Exists(itemsPath))
            LoadItems(itemsPath);

        // 기본 세이브 파일 로드
        var savePath = @"C:\Users\ocean\Desktop\대항해시대3\savedata.cds";
        if (System.IO.File.Exists(savePath))
            LoadSaveFile(savePath);
        else
            StatusText = "준비됨";
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

            ApplyCityFilter();
            StatusText = $"도시 로드 완료: {_allCities.Count}개";
        }
        catch (Exception ex)
        {
            StatusText = "도시 로드 실패";
            System.Windows.MessageBox.Show($"도시 로드 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private async Task LoadBooksFromDbAsync()
    {
        try
        {
            _allBooks = await _bookService.LoadBooksFromDbAsync();

            Languages.Clear();
            foreach (var lang in _bookService.GetDistinctLanguages(_allBooks))
                Languages.Add(lang);

            RequiredSkills.Clear();
            foreach (var skill in _bookService.GetDistinctRequiredSkills(_allBooks))
                RequiredSkills.Add(skill);

            ApplyBookFilter();
            StatusText = $"도서 로드 완료: {_allBooks.Count}개";
        }
        catch (Exception ex)
        {
            StatusText = "도서 로드 실패";
            System.Windows.MessageBox.Show($"도서 로드 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    #region Load Methods

    public void LoadSaveFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "세이브 파일 (*.CDS)|*.CDS|모든 파일 (*.*)|*.*",
            Title = "세이브 파일 선택"
        };

        if (dialog.ShowDialog() == true)
        {
            LoadSaveFile(dialog.FileName);
        }
    }

    public void LoadSaveFile(string filePath)
    {
        try
        {
            StatusText = "파일 읽는 중...";
            _saveDataService.SetCities(_allCities);
            _saveGameInfo = _saveDataService.ReadSaveFile(filePath);
            _allCharacters = _saveGameInfo.Characters;

            FilePath = $"파일 경로: {filePath}";
            WindowTitle = $"대항해시대3 세이브 뷰어 - {_saveGameInfo.DateString}";

            ApplyCharacterFilter();
            ApplyPatronFilter();
        }
        catch (Exception ex)
        {
            StatusText = "로드 실패";
            System.Windows.MessageBox.Show($"파일 읽기 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    public void LoadPatrons(string filePath)
    {
        try
        {
            _allPatrons = _patronService.LoadPatrons(filePath);

            Nationalities.Clear();
            foreach (var nat in _patronService.GetDistinctNationalities(_allPatrons))
                Nationalities.Add(nat);

            ApplyPatronFilter();
            StatusText = $"후원자 로드 완료: {_allPatrons.Count}명";
        }
        catch (Exception ex)
        {
            StatusText = "후원자 로드 실패";
            System.Windows.MessageBox.Show($"후원자 파일 읽기 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    public void LoadFigureheads(string filePath)
    {
        try
        {
            _allFigureheads = _figureheadService.LoadFigureheads(filePath);

            FigureheadFunctions.Clear();
            foreach (var func in _figureheadService.GetDistinctFunctions())
                FigureheadFunctions.Add(func);

            FigureheadLevels.Clear();
            foreach (var level in _figureheadService.GetDistinctLevels())
                FigureheadLevels.Add(level);

            ApplyFigureheadFilter();
            StatusText = $"선수상 로드 완료: {_allFigureheads.Count}개";
        }
        catch (Exception ex)
        {
            StatusText = "선수상 로드 실패";
            System.Windows.MessageBox.Show($"선수상 파일 읽기 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    public void LoadItems(string filePath)
    {
        try
        {
            _allItems = _itemService.LoadItems(filePath);

            ItemCategories.Clear();
            foreach (var category in _itemService.GetDistinctCategories())
                ItemCategories.Add(category);

            ApplyItemFilter();
            StatusText = $"아이템 로드 완료: {_allItems.Count}개";
        }
        catch (Exception ex)
        {
            StatusText = "아이템 로드 실패";
            System.Windows.MessageBox.Show($"아이템 파일 읽기 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    #endregion

    #region Filter Methods

    private void ApplyCharacterFilter()
    {
        if (_allCharacters.Count == 0) return;

        var filtered = _characterService.Filter(
            _allCharacters,
            ShowGrayCharacters,
            string.IsNullOrWhiteSpace(CharacterNameSearch) ? null : CharacterNameSearch,
            FilterBySkill ? SelectedSkillIndex + 1 : null,
            FilterBySkill ? SelectedSkillLevel : null);

        Characters = new ObservableCollection<CharacterData>(filtered);

        var grayCount = _allCharacters.Count(c => c.IsGray);
        var normalCount = _allCharacters.Count - grayCount;

        StatusText = ShowGrayCharacters
            ? $"로드 완료: {_allCharacters.Count}개 (등장: {normalCount}, 미등장: {grayCount})"
            : $"로드 완료: {normalCount}개 (미등장 {grayCount}개 숨김)";
    }

    private void ApplyBookFilter()
    {
        if (_allBooks.Count == 0) return;

        var filtered = _bookService.Filter(
            _allBooks,
            string.IsNullOrWhiteSpace(BookNameSearch) ? null : BookNameSearch,
            string.IsNullOrWhiteSpace(LibrarySearch) ? null : LibrarySearch,
            string.IsNullOrWhiteSpace(HintSearch) ? null : HintSearch,
            SelectedLanguage,
            SelectedRequiredSkill);

        Books = new ObservableCollection<Book>(filtered);
        StatusText = $"도서: {filtered.Count}개";
    }

    private void ApplyCityFilter()
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

    private void ApplyPatronFilter()
    {
        if (_allPatrons.Count == 0) return;

        var filtered = _patronService.Filter(
            _allPatrons,
            string.IsNullOrWhiteSpace(PatronNameSearch) ? null : PatronNameSearch,
            string.IsNullOrWhiteSpace(PatronCitySearch) ? null : PatronCitySearch,
            SelectedNationality,
            ActivePatronsOnly,
            CurrentYear);

        var displayList = _patronService.ToDisplayList(filtered, CurrentYear);
        Patrons = new ObservableCollection<PatronDisplay>(displayList);
        StatusText = $"후원자: {displayList.Count}명 (기준년도: {CurrentYear})";
    }

    private void ApplyFigureheadFilter()
    {
        if (_allFigureheads.Count == 0) return;

        int? level = null;
        if (!string.IsNullOrWhiteSpace(SelectedFigureheadLevel) && SelectedFigureheadLevel != "전체")
        {
            if (int.TryParse(SelectedFigureheadLevel, out var parsed))
                level = parsed;
        }

        var filtered = _figureheadService.Filter(
            string.IsNullOrWhiteSpace(FigureheadNameSearch) ? null : FigureheadNameSearch,
            SelectedFigureheadFunction,
            level);

        Figureheads = new ObservableCollection<Figurehead>(filtered);
        StatusText = $"선수상: {filtered.Count}개";
    }

    private void ApplyItemFilter()
    {
        if (_allItems.Count == 0) return;

        var filtered = _itemService.Filter(
            string.IsNullOrWhiteSpace(ItemNameSearch) ? null : ItemNameSearch,
            SelectedItemCategory,
            string.IsNullOrWhiteSpace(ItemDiscoverySearch) ? null : ItemDiscoverySearch);

        Items = new ObservableCollection<Item>(filtered);
        StatusText = $"아이템: {filtered.Count}개";
    }

    #endregion

    #region Reset Methods

    private void ResetBookFilter()
    {
        BookNameSearch = "";
        LibrarySearch = "";
        HintSearch = "";
        SelectedLanguage = null;
        SelectedRequiredSkill = null;
    }

    private void ResetCityFilter()
    {
        CityNameSearch = "";
        SelectedCulturalSphere = "전체";
        LibraryOnly = false;
        ShipyardOnly = false;
    }

    private void ResetPatronFilter()
    {
        PatronNameSearch = "";
        PatronCitySearch = "";
        SelectedNationality = null;
        ActivePatronsOnly = false;
    }

    private void ResetFigureheadFilter()
    {
        FigureheadNameSearch = "";
        SelectedFigureheadFunction = null;
        SelectedFigureheadLevel = null;
    }

    private void ResetItemFilter()
    {
        ItemNameSearch = "";
        SelectedItemCategory = null;
        ItemDiscoverySearch = "";
    }

    #endregion

    #region City Edit Methods

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
            System.Windows.MessageBox.Show($"도시 정보를 저장했습니다.\n\n{dialog.FileName}",
                "완료", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"내보내기 실패: {ex.Message}", "오류",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private async void EditCityPixel(City? city)
    {
        if (city == null) return;

        var dialog = new CdsHelper.Form.UI.Views.EditCityPixelDialog(
            city.Name, city.PixelX, city.PixelY, city.HasLibrary, city.Latitude, city.Longitude, city.CulturalSphere);

        dialog.Owner = System.Windows.Application.Current.MainWindow;

        if (dialog.ShowDialog() != true) return;

        try
        {
            var result = await _cityService.UpdateCityInfoAsync(
                city.Id, dialog.CityName, dialog.PixelX, dialog.PixelY, dialog.HasLibrary, dialog.Latitude, dialog.Longitude, dialog.CulturalSphere);

            if (result)
            {
                // UI 갱신
                city.Name = dialog.CityName;
                city.PixelX = dialog.PixelX;
                city.PixelY = dialog.PixelY;
                city.HasLibrary = dialog.HasLibrary;
                city.Latitude = dialog.Latitude;
                city.Longitude = dialog.Longitude;
                city.CulturalSphere = dialog.CulturalSphere;
                ApplyCityFilter();
                StatusText = $"{dialog.CityName} 정보 업데이트 완료: 문화권 {dialog.CulturalSphere ?? "-"}, 도서관: {(dialog.HasLibrary ? "있음" : "없음")}";
            }
            else
            {
                System.Windows.MessageBox.Show("도시 정보를 찾을 수 없습니다.", "오류",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"업데이트 실패: {ex.Message}", "오류",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    #endregion
}
