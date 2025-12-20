using System.Collections.ObjectModel;
using System.Windows.Input;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;
using Prism.Commands;
using Prism.Mvvm;

namespace CdsHelper.Main.Local.ViewModels;

public class PatronContentViewModel : BindableBase
{
    private readonly PatronService _patronService;
    private readonly SaveDataService _saveDataService;

    private List<Patron> _allPatrons = new();
    private SaveGameInfo? _saveGameInfo;

    #region Collections

    private ObservableCollection<PatronDisplay> _patrons = new();
    public ObservableCollection<PatronDisplay> Patrons
    {
        get => _patrons;
        set => SetProperty(ref _patrons, value);
    }

    #endregion

    #region Filter Properties

    private string _patronNameSearch = "";
    public string PatronNameSearch
    {
        get => _patronNameSearch;
        set { SetProperty(ref _patronNameSearch, value); ApplyFilter(); }
    }

    private string _patronCitySearch = "";
    public string PatronCitySearch
    {
        get => _patronCitySearch;
        set { SetProperty(ref _patronCitySearch, value); ApplyFilter(); }
    }

    private string? _selectedNationality;
    public string? SelectedNationality
    {
        get => _selectedNationality;
        set { SetProperty(ref _selectedNationality, value); ApplyFilter(); }
    }

    private bool _activePatronsOnly;
    public bool ActivePatronsOnly
    {
        get => _activePatronsOnly;
        set { SetProperty(ref _activePatronsOnly, value); ApplyFilter(); }
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

    public int CurrentYear => _saveGameInfo?.Year ?? 1480;

    #endregion

    #region Commands

    public ICommand ResetFilterCommand { get; }
    public ICommand LoadSaveCommand { get; }

    #endregion

    public PatronContentViewModel(
        PatronService patronService,
        SaveDataService saveDataService)
    {
        _patronService = patronService;
        _saveDataService = saveDataService;

        ResetFilterCommand = new DelegateCommand(ResetFilter);
        LoadSaveCommand = new DelegateCommand(LoadSaveFile);

        Initialize();
    }

    private void Initialize()
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var patronsPath = System.IO.Path.Combine(basePath, "patrons.json");

        if (System.IO.File.Exists(patronsPath))
            LoadPatrons(patronsPath);

        // 기본 세이브 파일 로드
        var savePath = @"C:\Users\ocean\Desktop\대항해시대3\savedata.cds";
        if (System.IO.File.Exists(savePath))
            LoadSaveFile(savePath);
    }

    private void LoadPatrons(string filePath)
    {
        try
        {
            _allPatrons = _patronService.LoadPatrons(filePath);

            Nationalities.Clear();
            foreach (var nat in _patronService.GetDistinctNationalities(_allPatrons))
                Nationalities.Add(nat);

            ApplyFilter();
            StatusText = $"후원자 로드 완료: {_allPatrons.Count}명";
        }
        catch (Exception ex)
        {
            StatusText = "후원자 로드 실패";
            System.Windows.MessageBox.Show($"후원자 파일 읽기 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

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
            _saveGameInfo = _saveDataService.ReadSaveFile(filePath);
            RaisePropertyChanged(nameof(CurrentYear));
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusText = "로드 실패";
            System.Windows.MessageBox.Show($"파일 읽기 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void ApplyFilter()
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

    private void ResetFilter()
    {
        PatronNameSearch = "";
        PatronCitySearch = "";
        SelectedNationality = null;
        ActivePatronsOnly = false;
    }
}
