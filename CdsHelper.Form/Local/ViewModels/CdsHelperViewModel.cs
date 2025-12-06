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
    private readonly SaveDataService _saveDataService;

    // Raw data
    private List<CharacterData> _allCharacters = new();
    private List<Book> _allBooks = new();
    private List<City> _allCities = new();
    private List<Patron> _allPatrons = new();
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

    private int _selectedSkillIndex = 1;
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

    #endregion

    public CdsHelperViewModel(
        CharacterService characterService,
        BookService bookService,
        CityService cityService,
        PatronService patronService,
        SaveDataService saveDataService)
    {
        _characterService = characterService;
        _bookService = bookService;
        _cityService = cityService;
        _patronService = patronService;
        _saveDataService = saveDataService;

        LoadSaveCommand = new DelegateCommand(LoadSaveFile);
        ResetBookFilterCommand = new DelegateCommand(ResetBookFilter);
        ResetCityFilterCommand = new DelegateCommand(ResetCityFilter);
        ResetPatronFilterCommand = new DelegateCommand(ResetPatronFilter);

        // 앱 시작 시 데이터 로드
        Initialize();
    }

    private void Initialize()
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;

        var booksPath = System.IO.Path.Combine(basePath, "books.json");
        var citiesPath = System.IO.Path.Combine(basePath, "cities.json");
        var patronsPath = System.IO.Path.Combine(basePath, "patrons.json");

        if (System.IO.File.Exists(citiesPath))
            LoadCities(citiesPath);

        if (System.IO.File.Exists(booksPath))
            LoadBooks(booksPath);

        if (System.IO.File.Exists(patronsPath))
            LoadPatrons(patronsPath);

        // 기본 세이브 파일 로드
        var savePath = @"C:\Users\ocean\Desktop\대항해시대3\savedata.cds";
        if (System.IO.File.Exists(savePath))
            LoadSaveFile(savePath);
        else
            StatusText = "준비됨";
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

    public void LoadBooks(string filePath)
    {
        try
        {
            _allBooks = _bookService.LoadBooks(filePath);

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
            System.Windows.MessageBox.Show($"도서 파일 읽기 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    public void LoadCities(string filePath)
    {
        try
        {
            _allCities = _cityService.LoadCities(filePath);

            CulturalSpheres.Clear();
            foreach (var cs in _cityService.GetDistinctCulturalSpheres(_allCities))
                CulturalSpheres.Add(cs);

            ApplyCityFilter();
            StatusText = $"도시 로드 완료: {_allCities.Count}개";
        }
        catch (Exception ex)
        {
            StatusText = "도시 로드 실패";
            System.Windows.MessageBox.Show($"도시 파일 읽기 실패:\n\n{ex.Message}",
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

    #endregion

    #region Filter Methods

    private void ApplyCharacterFilter()
    {
        if (_allCharacters.Count == 0) return;

        var filtered = _characterService.Filter(
            _allCharacters,
            ShowGrayCharacters,
            string.IsNullOrWhiteSpace(CharacterNameSearch) ? null : CharacterNameSearch,
            FilterBySkill ? SelectedSkillIndex : null,
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
            SelectedCulturalSphere,
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
        SelectedCulturalSphere = null;
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

    #endregion
}
