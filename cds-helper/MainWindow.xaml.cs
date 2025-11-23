using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using cds_helper.Models;
using cds_helper.Services;

namespace cds_helper;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly SaveDataReader _saveDataReader;
    private readonly BookService _bookService;
    private readonly CityService _cityService;
    private readonly PatronService _patronService;
    private readonly string[] _savePaths = new[]
    {
        @"SAVEDATA.CDS",
        @"C:\Users\ocean\Desktop\대항해시대3\SAVEDATA.CDS"
    };
    private readonly string _booksPath = @"books.json";
    private readonly string _citiesPath = @"cities.json";
    private readonly string _patronsPath = @"patrons.json";

    private SaveGameInfo? _saveGameInfo;
    private List<CharacterData> _allCharacters = new();
    private List<Book> _allBooks = new();
    private List<City> _allCities = new();
    private List<Patron> _allPatrons = new();

    // 지도 드래그 관련 변수
    private bool _isDragging = false;
    private Point _dragStartPoint;

    private static readonly Dictionary<int, string> SkillNames = new()
    {
        { 1, "항해술" },
        { 2, "운용술" },
        { 3, "검술" },
        { 4, "포술" },
        { 5, "사격술" },
        { 6, "의학" },
        { 7, "웅변술" },
        { 8, "측량술" },
        { 9, "역사학" },
        { 10, "회피" },
        { 11, "조선술" },
        { 12, "신학" },
        { 13, "과학" },
        { 14, "스페인어" },
        { 15, "포르투갈어" },
        { 16, "로망스어" },
        { 17, "게르만어" },
        { 18, "슬라브어" },
        { 19, "아랍어" },
        { 20, "페르시아어" },
        { 21, "중국어" },
        { 22, "힌두어" },
        { 23, "위그르어" },
        { 24, "아프리카 토착어" },
        { 25, "아메리카 토착어" },
        { 26, "동남아시아 토착어" },
        { 27, "동아시아 토착어" },
    };

    public MainWindow()
    {
        InitializeComponent();
        _saveDataReader = new SaveDataReader();
        _bookService = new BookService();
        _cityService = new CityService();
        _patronService = new PatronService();

        // 기본값: 미등장 캐릭터 숨김
        ChkShowGray.IsChecked = false;

        // 특기 ComboBox 초기화
        InitializeSkillComboBox();

        // 레벨 ComboBox 초기화
        InitializeLevelComboBox();

        // 도서 ComboBox 초기화
        InitializeBookComboBoxes();

        // 시작 시 자동으로 세이브 파일 로드 시도
        AutoLoadSaveFile();

        // 시작 시 자동으로 도서 파일 로드 시도
        AutoLoadBooks();

        // 시작 시 자동으로 도시 파일 로드 시도
        AutoLoadCities();

        // 시작 시 자동으로 후원자 파일 로드 시도
        AutoLoadPatrons();

        // 지도 이미지 로드
        LoadMapImage();
    }

    private void InitializeSkillComboBox()
    {
        CmbSkill.Items.Clear();
        foreach (var skill in SkillNames.OrderBy(s => s.Key))
        {
            CmbSkill.Items.Add(new ComboBoxItem
            {
                Content = $"{skill.Key}. {skill.Value}",
                Tag = skill.Key
            });
        }
        if (CmbSkill.Items.Count > 0)
            CmbSkill.SelectedIndex = 0;
    }

    private void InitializeLevelComboBox()
    {
        CmbLevel.Items.Clear();
        for (byte i = 1; i <= 9; i++)
        {
            CmbLevel.Items.Add(new ComboBoxItem
            {
                Content = i.ToString(),
                Tag = i
            });
        }
        if (CmbLevel.Items.Count > 0)
            CmbLevel.SelectedIndex = 2; // 기본값 3
    }

    private void AutoLoadSaveFile()
    {
        foreach (var path in _savePaths)
        {
            System.Diagnostics.Debug.WriteLine($"경로 확인: {path}");
            if (File.Exists(path))
            {
                System.Diagnostics.Debug.WriteLine($"파일 발견: {path}");
                LoadSaveFile(path);
                return;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"파일 없음: {path}");
            }
        }

        TxtStatus.Text = "세이브 파일을 찾을 수 없습니다. '세이브 파일 읽기' 버튼을 클릭하세요.";
    }

    private void BtnLoadSave_Click(object sender, RoutedEventArgs e)
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

    private void LoadSaveFile(string filePath)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"파일 로드 시작: {filePath}");
            TxtStatus.Text = "파일 읽는 중...";

            _saveGameInfo = _saveDataReader.ReadSaveFile(filePath);
            _allCharacters = _saveGameInfo.Characters;
            System.Diagnostics.Debug.WriteLine($"읽은 캐릭터 수: {_allCharacters.Count}");

            TxtFilePath.Text = $"파일 경로: {filePath}";

            // 제목 표시줄에 날짜 표시
            Title = $"대항해시대3 세이브 뷰어 - {_saveGameInfo.DateString}";

            // 필터링 적용
            ApplyFilter();

            // 후원자 필터도 다시 적용 (현재 년도 기준으로 상태가 바뀌므로)
            if (_allPatrons.Count > 0)
            {
                ApplyPatronFilter();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"오류 발생: {ex}");
            MessageBox.Show($"파일 읽기 실패:\n\n{ex.Message}\n\n{ex.StackTrace}",
                "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            TxtStatus.Text = "로드 실패";
        }
    }

    private void ApplyFilter()
    {
        var showGray = ChkShowGray.IsChecked ?? false;
        var filterSkill = ChkFilterSkill.IsChecked ?? false;
        var nameSearch = TxtNameSearch?.Text?.Trim() ?? "";

        var filtered = _allCharacters.AsEnumerable();

        // 회색 필터
        if (!showGray)
        {
            filtered = filtered.Where(c => !c.IsGray);
        }

        // 이름 검색 필터
        if (!string.IsNullOrEmpty(nameSearch))
        {
            filtered = filtered.Where(c => c.Name.Contains(nameSearch, StringComparison.OrdinalIgnoreCase));
        }

        // 특기 필터
        if (filterSkill && CmbSkill.SelectedItem is ComboBoxItem skillItem && CmbLevel.SelectedItem is ComboBoxItem levelItem)
        {
            int skillIndex = (int)skillItem.Tag;
            byte level = (byte)levelItem.Tag;
            filtered = filtered.Where(c => c.HasSkill(skillIndex, level));
        }

        var filteredList = filtered.ToList();
        DgCharacters.ItemsSource = filteredList;

        var grayCount = _allCharacters.Count(c => c.IsGray);
        var normalCount = _allCharacters.Count - grayCount;

        string statusText;
        var filterParts = new List<string>();

        if (!string.IsNullOrEmpty(nameSearch))
            filterParts.Add($"이름: {nameSearch}");

        if (filterSkill && CmbSkill.SelectedItem is ComboBoxItem selectedSkill && CmbLevel.SelectedItem is ComboBoxItem selectedLevel)
        {
            var skillName = SkillNames[(int)selectedSkill.Tag];
            var levelValue = (byte)selectedLevel.Tag;
            filterParts.Add($"{skillName} Lv{levelValue}");
        }

        if (filterParts.Count > 0)
        {
            statusText = $"필터 적용: {filteredList.Count}개 ({string.Join(", ", filterParts)})";
        }
        else
        {
            statusText = showGray
                ? $"로드 완료: {_allCharacters.Count}개 (등장: {normalCount}, 미등장: {grayCount})"
                : $"로드 완료: {normalCount}개 (미등장 {grayCount}개 숨김)";
        }

        TxtStatus.Text = statusText;
    }

    private void ChkShowGray_Changed(object sender, RoutedEventArgs e)
    {
        if (_allCharacters.Count > 0)
        {
            ApplyFilter();
        }
    }

    private void TxtNameSearch_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_allCharacters.Count > 0)
        {
            ApplyFilter();
        }
    }

    private void ChkFilterSkill_Changed(object sender, RoutedEventArgs e)
    {
        if (_allCharacters.Count > 0)
        {
            ApplyFilter();
        }
    }

    private void CmbSkill_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_allCharacters.Count > 0 && (ChkFilterSkill.IsChecked ?? false))
        {
            ApplyFilter();
        }
    }

    private void CmbLevel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_allCharacters.Count > 0 && (ChkFilterSkill.IsChecked ?? false))
        {
            ApplyFilter();
        }
    }

    // === 도서 관련 메서드 ===

    private void InitializeBookComboBoxes()
    {
        // 언어 ComboBox 초기화
        CmbLanguage.Items.Clear();
        CmbLanguage.Items.Add(new ComboBoxItem { Content = "전체", Tag = "" });

        // 필요 스킬 ComboBox 초기화
        CmbRequiredSkill.Items.Clear();
        CmbRequiredSkill.Items.Add(new ComboBoxItem { Content = "전체", Tag = "" });

        CmbLanguage.SelectedIndex = 0;
        CmbRequiredSkill.SelectedIndex = 0;
    }

    private void AutoLoadBooks()
    {
        try
        {
            // 실행 파일 위치에서 books.json 찾기
            var executablePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var executableDir = System.IO.Path.GetDirectoryName(executablePath);
            var booksFilePath = System.IO.Path.Combine(executableDir!, _booksPath);

            if (!File.Exists(booksFilePath))
            {
                // 프로젝트 루트에서 찾기
                var projectRoot = System.IO.Path.Combine(executableDir!, "..", "..", "..", "..");
                booksFilePath = System.IO.Path.Combine(projectRoot, "cds-helper", _booksPath);
            }

            if (File.Exists(booksFilePath))
            {
                LoadBooks(booksFilePath);
            }
            else
            {
                TxtStatus.Text = "books.json 파일을 찾을 수 없습니다.";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"도서 자동 로드 오류: {ex}");
        }
    }

    private void LoadBooks(string filePath)
    {
        try
        {
            _allBooks = _bookService.LoadBooks(filePath);

            // 언어 목록 구성
            var languages = _allBooks
                .Select(b => b.Language)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Distinct()
                .OrderBy(l => l)
                .ToList();

            CmbLanguage.Items.Clear();
            CmbLanguage.Items.Add(new ComboBoxItem { Content = "전체", Tag = "" });
            foreach (var lang in languages)
            {
                CmbLanguage.Items.Add(new ComboBoxItem { Content = lang, Tag = lang });
            }
            CmbLanguage.SelectedIndex = 0;

            // 필요 스킬 목록 구성
            var requiredSkills = _allBooks
                .Select(b => b.Required)
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct()
                .OrderBy(r => r)
                .ToList();

            CmbRequiredSkill.Items.Clear();
            CmbRequiredSkill.Items.Add(new ComboBoxItem { Content = "전체", Tag = "" });
            foreach (var skill in requiredSkills)
            {
                CmbRequiredSkill.Items.Add(new ComboBoxItem { Content = skill, Tag = skill });
            }
            CmbRequiredSkill.SelectedIndex = 0;

            // 필터 적용
            ApplyBookFilter();

            TxtStatus.Text = $"도서 로드 완료: {_allBooks.Count}개";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"도서 로드 오류: {ex}");
            MessageBox.Show($"도서 파일 읽기 실패:\n\n{ex.Message}",
                "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyBookFilter()
    {
        var nameSearch = TxtBookSearch?.Text?.Trim() ?? "";
        var librarySearch = TxtLibrarySearch?.Text?.Trim() ?? "";
        var hintSearch = TxtHintSearch?.Text?.Trim() ?? "";
        var selectedLanguage = (CmbLanguage.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        var selectedSkill = (CmbRequiredSkill.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";

        var filtered = _allBooks.AsEnumerable();

        // 도서명 검색
        if (!string.IsNullOrEmpty(nameSearch))
        {
            filtered = filtered.Where(b => b.Name.Contains(nameSearch, StringComparison.OrdinalIgnoreCase));
        }

        // 소재 도서관 검색
        if (!string.IsNullOrEmpty(librarySearch))
        {
            filtered = filtered.Where(b => b.Library.Contains(librarySearch, StringComparison.OrdinalIgnoreCase));
        }

        // 게제 힌트 검색
        if (!string.IsNullOrEmpty(hintSearch))
        {
            filtered = filtered.Where(b => b.Hint.Contains(hintSearch, StringComparison.OrdinalIgnoreCase));
        }

        // 언어 필터
        if (!string.IsNullOrEmpty(selectedLanguage))
        {
            filtered = filtered.Where(b => b.Language.Equals(selectedLanguage, StringComparison.OrdinalIgnoreCase));
        }

        // 필요 스킬 필터
        if (!string.IsNullOrEmpty(selectedSkill))
        {
            filtered = filtered.Where(b => b.Required.Equals(selectedSkill, StringComparison.OrdinalIgnoreCase));
        }

        var filteredList = filtered.ToList();
        DgBooks.ItemsSource = filteredList;

        var filterParts = new List<string>();
        if (!string.IsNullOrEmpty(nameSearch))
            filterParts.Add($"도서명: {nameSearch}");
        if (!string.IsNullOrEmpty(librarySearch))
            filterParts.Add($"소재 도서관: {librarySearch}");
        if (!string.IsNullOrEmpty(hintSearch))
            filterParts.Add($"게제 힌트: {hintSearch}");
        if (!string.IsNullOrEmpty(selectedLanguage))
            filterParts.Add($"언어: {selectedLanguage}");
        if (!string.IsNullOrEmpty(selectedSkill))
            filterParts.Add($"필요: {selectedSkill}");

        if (filterParts.Count > 0)
        {
            TxtStatus.Text = $"필터 적용: {filteredList.Count}개 ({string.Join(", ", filterParts)})";
        }
        else
        {
            TxtStatus.Text = $"도서 로드 완료: {_allBooks.Count}개";
        }
    }

    private void TxtBookSearch_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_allBooks.Count > 0)
        {
            ApplyBookFilter();
        }
    }

    private void TxtHintSearch_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_allBooks.Count > 0)
        {
            ApplyBookFilter();
        }
    }

    private void TxtLibrarySearch_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_allBooks.Count > 0)
        {
            ApplyBookFilter();
        }
    }

    private void CmbLanguage_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_allBooks.Count > 0)
        {
            ApplyBookFilter();
        }
    }

    private void CmbRequiredSkill_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_allBooks.Count > 0)
        {
            ApplyBookFilter();
        }
    }

    private void BtnResetBookFilter_Click(object sender, RoutedEventArgs e)
    {
        TxtBookSearch.Text = "";
        TxtLibrarySearch.Text = "";
        TxtHintSearch.Text = "";
        CmbLanguage.SelectedIndex = 0;
        CmbRequiredSkill.SelectedIndex = 0;
        ApplyBookFilter();
    }

    // === 도시 관련 메서드 ===

    private void AutoLoadCities()
    {
        try
        {
            // 실행 파일 위치에서 cities.json 찾기
            var executablePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var executableDir = System.IO.Path.GetDirectoryName(executablePath);
            var citiesFilePath = System.IO.Path.Combine(executableDir!, _citiesPath);

            if (!File.Exists(citiesFilePath))
            {
                // 프로젝트 루트에서 찾기
                var projectRoot = System.IO.Path.Combine(executableDir!, "..", "..", "..", "..");
                citiesFilePath = System.IO.Path.Combine(projectRoot, "cds-helper", _citiesPath);
            }

            if (File.Exists(citiesFilePath))
            {
                LoadCities(citiesFilePath);
            }
            else
            {
                TxtStatus.Text = "cities.json 파일을 찾을 수 없습니다.";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"도시 자동 로드 오류: {ex}");
        }
    }

    private void LoadCities(string filePath)
    {
        try
        {
            _allCities = _cityService.LoadCities(filePath);

            // 문화권 목록 구성
            var culturalSpheres = _allCities
                .Select(c => c.CulturalSphere)
                .Where(cs => !string.IsNullOrWhiteSpace(cs))
                .Distinct()
                .OrderBy(cs => cs)
                .ToList();

            CmbCulturalSphere.Items.Clear();
            CmbCulturalSphere.Items.Add(new ComboBoxItem { Content = "전체", Tag = "" });
            foreach (var cs in culturalSpheres)
            {
                CmbCulturalSphere.Items.Add(new ComboBoxItem { Content = cs, Tag = cs });
            }
            CmbCulturalSphere.SelectedIndex = 0;

            // 필터 적용
            ApplyCityFilter();

            TxtStatus.Text = $"도시 로드 완료: {_allCities.Count}개";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"도시 로드 오류: {ex}");
            MessageBox.Show($"도시 파일 읽기 실패:\n\n{ex.Message}",
                "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyCityFilter()
    {
        var nameSearch = TxtCitySearch?.Text?.Trim() ?? "";
        var selectedCulturalSphere = (CmbCulturalSphere?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        var libraryOnly = ChkLibraryOnly?.IsChecked ?? false;
        var shipyardOnly = ChkShipyardOnly?.IsChecked ?? false;

        var filtered = _allCities.AsEnumerable();

        // 도시명 검색
        if (!string.IsNullOrEmpty(nameSearch))
        {
            filtered = filtered.Where(c => c.Name.Contains(nameSearch, StringComparison.OrdinalIgnoreCase));
        }

        // 문화권 필터
        if (!string.IsNullOrEmpty(selectedCulturalSphere))
        {
            filtered = filtered.Where(c => c.CulturalSphere != null && c.CulturalSphere.Equals(selectedCulturalSphere, StringComparison.OrdinalIgnoreCase));
        }

        // 도서관 있는 도시만 필터
        if (libraryOnly)
        {
            filtered = filtered.Where(c => c.HasLibrary);
        }

        // 조선소 있는 도시만 필터
        if (shipyardOnly)
        {
            filtered = filtered.Where(c => c.HasShipyard);
        }

        var filteredList = filtered.ToList();
        DgCities.ItemsSource = filteredList;

        var filterParts = new List<string>();
        if (!string.IsNullOrEmpty(nameSearch))
            filterParts.Add($"도시명: {nameSearch}");
        if (!string.IsNullOrEmpty(selectedCulturalSphere))
            filterParts.Add($"문화권: {selectedCulturalSphere}");
        if (libraryOnly)
            filterParts.Add("도서관 있는 도시만");
        if (shipyardOnly)
            filterParts.Add("조선소 있는 도시만");

        if (filterParts.Count > 0)
        {
            TxtStatus.Text = $"필터 적용: {filteredList.Count}개 ({string.Join(", ", filterParts)})";
        }
        else
        {
            TxtStatus.Text = $"도시 로드 완료: {_allCities.Count}개";
        }
    }

    private void TxtCitySearch_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_allCities.Count > 0)
        {
            ApplyCityFilter();
        }
    }

    private void ChkLibraryOnly_Changed(object sender, RoutedEventArgs e)
    {
        if (_allCities.Count > 0)
        {
            ApplyCityFilter();
        }
    }

    private void ChkShipyardOnly_Changed(object sender, RoutedEventArgs e)
    {
        if (_allCities.Count > 0)
        {
            ApplyCityFilter();
        }
    }

    private void CmbCulturalSphere_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_allCities.Count > 0)
        {
            ApplyCityFilter();
        }
    }

    private void BtnResetCityFilter_Click(object sender, RoutedEventArgs e)
    {
        TxtCitySearch.Text = "";
        CmbCulturalSphere.SelectedIndex = 0;
        ChkLibraryOnly.IsChecked = false;
        ChkShipyardOnly.IsChecked = false;
        ApplyCityFilter();
    }

    // === 후원자 관련 메서드 ===

    private void AutoLoadPatrons()
    {
        try
        {
            // 실행 파일 위치에서 patrons.json 찾기
            var executablePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var executableDir = System.IO.Path.GetDirectoryName(executablePath);
            var patronsFilePath = System.IO.Path.Combine(executableDir!, _patronsPath);

            if (!File.Exists(patronsFilePath))
            {
                // 프로젝트 루트에서 찾기
                var projectRoot = System.IO.Path.Combine(executableDir!, "..", "..", "..", "..");
                patronsFilePath = System.IO.Path.Combine(projectRoot, "cds-helper", _patronsPath);
            }

            if (File.Exists(patronsFilePath))
            {
                LoadPatrons(patronsFilePath);
            }
            else
            {
                TxtStatus.Text = "patrons.json 파일을 찾을 수 없습니다.";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"후원자 자동 로드 오류: {ex}");
        }
    }

    private void LoadPatrons(string filePath)
    {
        try
        {
            _allPatrons = _patronService.LoadPatrons(filePath);

            // 국적 목록 구성
            var nationalities = _allPatrons
                .Select(p => p.Nationality)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            CmbPatronNationality.Items.Clear();
            CmbPatronNationality.Items.Add(new ComboBoxItem { Content = "전체", Tag = "" });
            foreach (var nationality in nationalities)
            {
                CmbPatronNationality.Items.Add(new ComboBoxItem { Content = nationality, Tag = nationality });
            }
            CmbPatronNationality.SelectedIndex = 0;

            // 필터 적용
            ApplyPatronFilter();

            TxtStatus.Text = $"후원자 로드 완료: {_allPatrons.Count}명";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"후원자 로드 오류: {ex}");
            MessageBox.Show($"후원자 파일 읽기 실패:\n\n{ex.Message}",
                "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyPatronFilter()
    {
        var nameSearch = TxtPatronSearch?.Text?.Trim() ?? "";
        var citySearch = TxtPatronCitySearch?.Text?.Trim() ?? "";
        var selectedNationality = (CmbPatronNationality?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        var activeOnly = ChkActiveOnly?.IsChecked ?? false;

        var currentYear = _saveGameInfo?.Year ?? 1480;

        var filtered = _allPatrons.AsEnumerable();

        // 후원자명 검색
        if (!string.IsNullOrEmpty(nameSearch))
        {
            filtered = filtered.Where(p => p.Name.Contains(nameSearch, StringComparison.OrdinalIgnoreCase));
        }

        // 도시 검색
        if (!string.IsNullOrEmpty(citySearch))
        {
            filtered = filtered.Where(p => p.City.Contains(citySearch, StringComparison.OrdinalIgnoreCase));
        }

        // 국적 필터
        if (!string.IsNullOrEmpty(selectedNationality))
        {
            filtered = filtered.Where(p => p.Nationality.Equals(selectedNationality, StringComparison.OrdinalIgnoreCase));
        }

        // 활동중인 후원자만 필터
        if (activeOnly)
        {
            filtered = filtered.Where(p => p.IsActive(currentYear));
        }

        // 각 후원자의 상태를 현재 년도 기준으로 계산
        var filteredList = filtered.Select(p => new
        {
            Patron = p,
            StatusDisplay = p.StatusDisplay(currentYear)
        }).ToList();

        // DataGrid에 바인딩할 수 있도록 익명 타입을 사용한 래퍼 생성
        var displayList = filteredList.Select(item => new PatronDisplay
        {
            Id = item.Patron.Id,
            Name = item.Patron.Name,
            Nationality = item.Patron.Nationality,
            City = item.Patron.City,
            Occupation = item.Patron.Occupation,
            SupportRate = item.Patron.SupportRate,
            Discernment = item.Patron.Discernment,
            AppearYear = item.Patron.AppearYear,
            RetireYear = item.Patron.RetireYear,
            StatusDisplay = item.StatusDisplay,
            Fame = item.Patron.Fame,
            Wealth = item.Patron.Wealth,
            Power = item.Patron.Power,
            Note = item.Patron.Note
        }).ToList();

        DgPatrons.ItemsSource = displayList;

        var filterParts = new List<string>();
        if (!string.IsNullOrEmpty(nameSearch))
            filterParts.Add($"후원자명: {nameSearch}");
        if (!string.IsNullOrEmpty(citySearch))
            filterParts.Add($"도시: {citySearch}");
        if (!string.IsNullOrEmpty(selectedNationality))
            filterParts.Add($"국적: {selectedNationality}");
        if (activeOnly)
            filterParts.Add($"활동중만 (기준년도: {currentYear})");

        if (filterParts.Count > 0)
        {
            TxtStatus.Text = $"필터 적용: {displayList.Count}명 ({string.Join(", ", filterParts)})";
        }
        else
        {
            TxtStatus.Text = $"후원자 로드 완료: {_allPatrons.Count}명 (기준년도: {currentYear})";
        }
    }

    // 후원자 표시용 클래스
    public class PatronDisplay
    {
        public int? Id { get; set; }
        public string Name { get; set; } = "";
        public string Nationality { get; set; } = "";
        public string City { get; set; } = "";
        public string Occupation { get; set; } = "";
        public string SupportRate { get; set; } = "";
        public int Discernment { get; set; }
        public int? AppearYear { get; set; }
        public int? RetireYear { get; set; }
        public string StatusDisplay { get; set; } = "";
        public int Fame { get; set; }
        public int Wealth { get; set; }
        public string Power { get; set; } = "";
        public string Note { get; set; } = "";
    }

    private void TxtPatronSearch_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_allPatrons.Count > 0)
        {
            ApplyPatronFilter();
        }
    }

    private void TxtPatronCitySearch_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_allPatrons.Count > 0)
        {
            ApplyPatronFilter();
        }
    }

    private void CmbPatronNationality_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_allPatrons.Count > 0)
        {
            ApplyPatronFilter();
        }
    }

    private void ChkActiveOnly_Changed(object sender, RoutedEventArgs e)
    {
        if (_allPatrons.Count > 0)
        {
            ApplyPatronFilter();
        }
    }

    private void BtnResetPatronFilter_Click(object sender, RoutedEventArgs e)
    {
        TxtPatronSearch.Text = "";
        TxtPatronCitySearch.Text = "";
        CmbPatronNationality.SelectedIndex = 0;
        ChkActiveOnly.IsChecked = false;
        ApplyPatronFilter();
    }

    #region 지도 탭 이벤트

    private void LoadMapImage()
    {
        try
        {
            var executableDir = AppDomain.CurrentDomain.BaseDirectory;
            var mapImagePath = System.IO.Path.Combine(executableDir, "대항해시대3-지도(발견물-이름-기준).jpg");

            if (File.Exists(mapImagePath))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(mapImagePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                ImgMap.Source = bitmap;

                // 지도 크기에 맞게 Canvas 크기 설정
                MapCanvas.Width = bitmap.PixelWidth;
                MapCanvas.Height = bitmap.PixelHeight;

                // 도시 표시
                DrawCities();

                TxtStatus.Text = "지도 로드 완료";
            }
            else
            {
                TxtStatus.Text = $"지도 파일을 찾을 수 없습니다: {mapImagePath}";
                MessageBox.Show($"지도 파일을 찾을 수 없습니다.\n경로: {mapImagePath}", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = "지도 로드 실패";
            MessageBox.Show($"지도 이미지를 로드하는 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DrawCities()
    {
        MapCanvas.Children.Clear();

        foreach (var city in _allCities)
        {
            if (city.PixelX.HasValue && city.PixelY.HasValue)
            {
                // 도시 점 그리기
                var ellipse = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = city.HasLibrary ? Brushes.Red : Brushes.Blue,
                    Stroke = Brushes.White,
                    StrokeThickness = 1
                };

                // 툴팁 추가 (마우스 커서 위쪽에 표시)
                var tooltip = new ToolTip
                {
                    Content = $"{city.Name}\n위도: {city.LatitudeDisplay}\n경도: {city.LongitudeDisplay}"
                };
                ellipse.ToolTip = tooltip;
                ToolTipService.SetPlacement(ellipse, System.Windows.Controls.Primitives.PlacementMode.Top);

                // Canvas에 위치 설정 (중앙 정렬)
                Canvas.SetLeft(ellipse, city.PixelX.Value - 4);
                Canvas.SetTop(ellipse, city.PixelY.Value - 4);

                MapCanvas.Children.Add(ellipse);
            }
        }

        TxtStatus.Text = $"지도 로드 완료 - 도시 {MapCanvas.Children.Count}개 표시";
    }

    private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
    {
        var currentScale = MapScaleTransform.ScaleX;
        var newScale = currentScale * 1.2;

        // 최대 5배까지만 확대
        if (newScale <= 5.0)
        {
            MapScaleTransform.ScaleX = newScale;
            MapScaleTransform.ScaleY = newScale;
            CanvasScaleTransform.ScaleX = newScale;
            CanvasScaleTransform.ScaleY = newScale;
            TxtStatus.Text = $"지도 확대: {newScale:F1}x";
        }
    }

    private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
    {
        var currentScale = MapScaleTransform.ScaleX;
        var newScale = currentScale / 1.2;

        // 최소 0.2배까지만 축소
        if (newScale >= 0.2)
        {
            MapScaleTransform.ScaleX = newScale;
            MapScaleTransform.ScaleY = newScale;
            CanvasScaleTransform.ScaleX = newScale;
            CanvasScaleTransform.ScaleY = newScale;
            TxtStatus.Text = $"지도 축소: {newScale:F1}x";
        }
    }

    private void BtnZoomReset_Click(object sender, RoutedEventArgs e)
    {
        MapScaleTransform.ScaleX = 1.0;
        MapScaleTransform.ScaleY = 1.0;
        CanvasScaleTransform.ScaleX = 1.0;
        CanvasScaleTransform.ScaleY = 1.0;
        TxtStatus.Text = "지도 원본 크기";
    }

    private void MapScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Ctrl 키가 눌려있을 때만 확대/축소
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            var currentScale = MapScaleTransform.ScaleX;
            double newScale;

            if (e.Delta > 0)
            {
                // 마우스 휠 위로 = 확대
                newScale = currentScale * 1.1;
                if (newScale > 5.0) newScale = 5.0; // 최대 5배
            }
            else
            {
                // 마우스 휠 아래로 = 축소
                newScale = currentScale / 1.1;
                if (newScale < 0.2) newScale = 0.2; // 최소 0.2배
            }

            MapScaleTransform.ScaleX = newScale;
            MapScaleTransform.ScaleY = newScale;
            CanvasScaleTransform.ScaleX = newScale;
            CanvasScaleTransform.ScaleY = newScale;
            TxtStatus.Text = $"지도 크기: {newScale:F1}x";

            // 기본 스크롤 동작 방지
            e.Handled = true;
        }
        // Ctrl 키가 안 눌려있으면 기본 스크롤 동작
    }

    private void MapScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 드래그 시작
        _isDragging = true;
        _dragStartPoint = e.GetPosition(MapScrollViewer);
        MapScrollViewer.Cursor = Cursors.Hand;
        MapScrollViewer.CaptureMouse();
        e.Handled = true;
    }

    private void MapScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            var currentPoint = e.GetPosition(MapScrollViewer);
            var offset = currentPoint - _dragStartPoint;

            // ScrollViewer의 스크롤 위치 조정
            MapScrollViewer.ScrollToHorizontalOffset(MapScrollViewer.HorizontalOffset - offset.X);
            MapScrollViewer.ScrollToVerticalOffset(MapScrollViewer.VerticalOffset - offset.Y);

            _dragStartPoint = currentPoint;
        }
    }

    private void MapScrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            // 드래그 종료
            _isDragging = false;
            MapScrollViewer.Cursor = Cursors.Arrow;
            MapScrollViewer.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void ImgMap_MouseMove(object sender, MouseEventArgs e)
    {
        if (ImgMap.Source != null)
        {
            // 이미지 상의 마우스 위치 가져오기
            var position = e.GetPosition(ImgMap);

            // 실제 이미지 크기
            var bitmap = ImgMap.Source as BitmapSource;
            if (bitmap != null)
            {
                // 현재 스케일 고려
                var scale = MapScaleTransform.ScaleX;

                // 이미지의 렌더링된 크기
                var renderedWidth = bitmap.PixelWidth * scale;
                var renderedHeight = bitmap.PixelHeight * scale;

                // RenderTransformOrigin이 0.5, 0.5이므로 중앙 기준
                // 실제 이미지 영역의 시작점 계산
                var offsetX = (renderedWidth - bitmap.PixelWidth * scale) / 2;
                var offsetY = (renderedHeight - bitmap.PixelHeight * scale) / 2;

                // 실제 픽셀 좌표로 변환
                var pixelX = (int)(position.X / scale);
                var pixelY = (int)(position.Y / scale);

                // 이미지 범위 내에 있는지 확인
                if (pixelX >= 0 && pixelX < bitmap.PixelWidth && pixelY >= 0 && pixelY < bitmap.PixelHeight)
                {
                    TxtMapCoordinates.Text = $"좌표: X={pixelX}, Y={pixelY}";
                }
                else
                {
                    TxtMapCoordinates.Text = "좌표: -";
                }
            }
        }
    }

    private void ImgMap_MouseLeave(object sender, MouseEventArgs e)
    {
        TxtMapCoordinates.Text = "좌표: -";
    }

    #endregion
}
