using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CdsHelper.Main.UI.Views;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using Prism.Commands;
using Prism.Mvvm;

namespace CdsHelper.Main.Local.ViewModel;

public class BookContentViewModel : BindableBase
{
    private readonly BookService _bookService;
    private readonly CityService _cityService;
    private List<Book> _allBooks = new();
    private List<City> _allCities = new();

    #region Collections

    private ObservableCollection<Book> _books = new();
    public ObservableCollection<Book> Books
    {
        get => _books;
        set => SetProperty(ref _books, value);
    }

    public ObservableCollection<string> Languages { get; } = new();
    public ObservableCollection<string> RequiredSkills { get; } = new();

    #endregion

    #region Selected Item

    private Book? _selectedBook;
    public Book? SelectedBook
    {
        get => _selectedBook;
        set => SetProperty(ref _selectedBook, value);
    }

    #endregion

    #region Filter Properties

    private string _bookNameSearch = "";
    public string BookNameSearch
    {
        get => _bookNameSearch;
        set { SetProperty(ref _bookNameSearch, value); ApplyFilter(); }
    }

    private string _librarySearch = "";
    public string LibrarySearch
    {
        get => _librarySearch;
        set { SetProperty(ref _librarySearch, value); ApplyFilter(); }
    }

    private string _hintSearch = "";
    public string HintSearch
    {
        get => _hintSearch;
        set { SetProperty(ref _hintSearch, value); ApplyFilter(); }
    }

    private string? _selectedLanguage;
    public string? SelectedLanguage
    {
        get => _selectedLanguage;
        set { SetProperty(ref _selectedLanguage, value); ApplyFilter(); }
    }

    private string? _selectedRequiredSkill;
    public string? SelectedRequiredSkill
    {
        get => _selectedRequiredSkill;
        set { SetProperty(ref _selectedRequiredSkill, value); ApplyFilter(); }
    }

    #endregion

    #region Status

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    #endregion

    #region Commands

    public ICommand ResetBookFilterCommand { get; }
    public ICommand EditLibraryMappingCommand { get; }

    #endregion

    public BookContentViewModel(BookService bookService, CityService cityService)
    {
        _bookService = bookService;
        _cityService = cityService;

        ResetBookFilterCommand = new DelegateCommand(ResetFilter);
        EditLibraryMappingCommand = new DelegateCommand(EditLibraryMapping, () => SelectedBook != null)
            .ObservesProperty(() => SelectedBook);

        Initialize();
    }

    private async void Initialize()
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var citiesPath = System.IO.Path.Combine(basePath, "cities.json");
        var dbPath = System.IO.Path.Combine(basePath, "cdshelper.db");
        var booksJsonPath = System.IO.Path.Combine(basePath, "books.json");

        if (System.IO.File.Exists(citiesPath))
            LoadCities(citiesPath);

        // BookService 초기화 (DB 및 JSON 마이그레이션)
        await _bookService.InitializeAsync(dbPath, booksJsonPath);

        await LoadBooksFromDbAsync();
    }

    private void LoadCities(string filePath)
    {
        try
        {
            _allCities = _cityService.LoadCities(filePath);
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

    private void ResetFilter()
    {
        BookNameSearch = "";
        LibrarySearch = "";
        HintSearch = "";
        SelectedLanguage = null;
        SelectedRequiredSkill = null;
    }

    private async void EditLibraryMapping()
    {
        if (SelectedBook == null) return;

        var dialog = new BookCityMappingDialog
        {
            Owner = Application.Current.MainWindow
        };

        dialog.Initialize(SelectedBook, _allCities);

        if (dialog.ShowDialog() == true)
        {
            var selectedCityIds = dialog.GetSelectedCityIds();

            try
            {
                // DB 업데이트
                await _bookService.UpdateBookCitiesAsync(SelectedBook.Id, selectedCityIds);

                // DB에서 다시 로드
                await LoadBooksFromDbAsync();

                StatusText = $"{SelectedBook.Name} 도서관 매핑 업데이트 완료";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"매핑 저장 실패: {ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
