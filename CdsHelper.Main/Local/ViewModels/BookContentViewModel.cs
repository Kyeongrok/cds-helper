using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CdsHelper.Main.UI.Views;
using CdsHelper.Support.Local.Events;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;
using Prism.Events;

namespace CdsHelper.Main.Local.ViewModel;

public partial class BookContentViewModel : ObservableObject
{
    private readonly BookService _bookService;
    private readonly CityService _cityService;
    private readonly SaveDataService _saveDataService;
    private readonly IEventAggregator _eventAggregator;
    private List<Book> _allBooks = new();
    private List<City> _allCities = new();
    private PlayerData? _playerData;

    #region Collections

    [ObservableProperty] private ObservableCollection<Book> _books = new();

    public ObservableCollection<string> Languages { get; } = new();
    public ObservableCollection<string> RequiredSkills { get; } = new();

    #endregion

    #region Selected Item

    [NotifyCanExecuteChangedFor(nameof(EditLibraryMappingCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteBookCommand))]
    [ObservableProperty] private Book? _selectedBook;

    #endregion

    #region Filter Properties

    [ObservableProperty] private string _bookNameSearch = "";
    partial void OnBookNameSearchChanged(string value) => ApplyFilter();

    [ObservableProperty] private string _librarySearch = "";
    partial void OnLibrarySearchChanged(string value) => ApplyFilter();

    [ObservableProperty] private string _hintSearch = "";
    partial void OnHintSearchChanged(string value) => ApplyFilter();

    [ObservableProperty] private string? _selectedLanguage;
    partial void OnSelectedLanguageChanged(string? value) => ApplyFilter();

    [ObservableProperty] private string? _selectedRequiredSkill;
    partial void OnSelectedRequiredSkillChanged(string? value) => ApplyFilter();

    #endregion

    #region Status

    [ObservableProperty] private string _statusText = "";

    #endregion

    public BookContentViewModel(
        BookService bookService,
        CityService cityService,
        SaveDataService saveDataService,
        IEventAggregator eventAggregator)
    {
        _bookService = bookService;
        _cityService = cityService;
        _saveDataService = saveDataService;
        _eventAggregator = eventAggregator;

        Initialize();

        eventAggregator.GetEvent<SaveDataLoadedEvent>().Subscribe(OnSaveDataLoaded);

        if (saveDataService.CurrentPlayerData != null)
            LoadSaveData(saveDataService.CurrentPlayerData);
    }

    private void OnSaveDataLoaded(SaveDataLoadedEventArgs args)
    {
        if (args.PlayerData != null)
            LoadSaveData(args.PlayerData);
    }

    public void LoadSaveData(PlayerData playerData)
    {
        try
        {
            StatusText = "도서 데이터 로드 중...";
            _playerData = playerData;

            UpdateBooksWithPlayerData();
            ApplyFilter();
            StatusText = $"도서 로드 완료: {Books.Count}개";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"도서 데이터 로드 실패:\n\n{ex.Message}",
                "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateBooksWithPlayerData()
    {
        if (_playerData == null) return;

        HashSet<int>? discoveredHintIds = null;
        HashSet<int>? hasHintIds = null;

        if (_saveDataService.CurrentSaveGameInfo?.Hints != null)
        {
            discoveredHintIds = _saveDataService.CurrentSaveGameInfo.Hints
                .Where(h => h.IsDiscovered)
                .Select(h => h.Index - 1)
                .ToHashSet();

            hasHintIds = _saveDataService.CurrentSaveGameInfo.Hints
                .Where(h => h.HasHint)
                .Select(h => h.Index - 1)
                .ToHashSet();
        }

        foreach (var book in _allBooks)
        {
            book.PlayerSkills = _playerData.Skills;
            book.PlayerLanguages = _playerData.Languages;
            book.DiscoveredHintIds = discoveredHintIds;
            book.HasHintIds = hasHintIds;
        }
    }

    private async void Initialize()
    {
        _allCities = _cityService.GetCachedCities();
        _allBooks = _bookService.GetCachedBooks();

        if (_allBooks.Count > 0)
        {
            LoadFromCache();
        }
        else
        {
            await LoadCitiesFromDbAsync();
            await LoadBooksFromDbAsync();
        }
    }

    private void LoadFromCache()
    {
        Languages.Clear();
        foreach (var lang in _bookService.GetDistinctLanguages(_allBooks))
            Languages.Add(lang);

        RequiredSkills.Clear();
        foreach (var skill in _bookService.GetDistinctRequiredSkills(_allBooks))
            RequiredSkills.Add(skill);

        ApplyFilter();
        StatusText = $"도서 로드 완료: {_allBooks.Count}개";
    }

    private async Task LoadCitiesFromDbAsync()
    {
        try
        {
            _allCities = await _cityService.LoadCitiesFromDbAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"도시 로드 실패: {ex.Message}");
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

            ApplyFilter();
            StatusText = $"도서 로드 완료: {_allBooks.Count}개";
        }
        catch (Exception ex)
        {
            StatusText = "도서 로드 실패";
            MessageBox.Show($"도서 로드 실패:\n\n{ex.Message}",
                "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyFilter()
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

    [RelayCommand]
    private void ResetBookFilter()
    {
        BookNameSearch = "";
        LibrarySearch = "";
        HintSearch = "";
        SelectedLanguage = null;
        SelectedRequiredSkill = null;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedBook))]
    private async void EditLibraryMapping()
    {
        if (SelectedBook == null) return;

        MessageBox.Show($"Book Id: {SelectedBook.Id}, Name: {SelectedBook.Name}");

        var dialog = new BookCityMappingDialog
        {
            Owner = Application.Current.MainWindow
        };

        dialog.Initialize(SelectedBook, _allCities);

        if (dialog.ShowDialog() == true)
        {
            var selectedCityIds = dialog.GetSelectedCityIds();
            var selectedCityNames = dialog.GetSelectedCityNames();

            try
            {
                await _bookService.UpdateBookCitiesAsync(SelectedBook.Id, selectedCityIds);
                await LoadBooksFromDbAsync();
                StatusText = $"{SelectedBook.Name} 도서관 매핑 업데이트 완료";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"매핑 저장 실패: {ex.Message}\n\n{ex.StackTrace}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedBook))]
    private async void DeleteBook()
    {
        if (SelectedBook == null) return;

        var result = MessageBox.Show(
            $"'{SelectedBook.Name}' 도서를 삭제하시겠습니까?",
            "도서 삭제", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _bookService.DeleteBookAsync(SelectedBook.Id);
            await LoadBooksFromDbAsync();
            StatusText = "도서 삭제 완료";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"도서 삭제 실패: {ex.Message}", "오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void NavigateToLibrary(byte? cityId)
    {
        if (!cityId.HasValue) return;

        var city = _allCities.FirstOrDefault(c => c.Id == cityId.Value);
        if (city == null) return;

        _eventAggregator.GetEvent<NavigateToCityEvent>().Publish(new NavigateToCityEventArgs
        {
            CityId = city.Id,
            CityName = city.Name,
            PixelX = city.PixelX,
            PixelY = city.PixelY
        });
    }

    private bool HasSelectedBook() => SelectedBook != null;
}
