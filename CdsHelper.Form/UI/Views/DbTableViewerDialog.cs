using System.Windows;
using System.Windows.Controls;
using CdsHelper.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CdsHelper.Form.UI.Views;

[TemplatePart(Name = PART_TableComboBox, Type = typeof(ComboBox))]
[TemplatePart(Name = PART_DataGrid, Type = typeof(DataGrid))]
[TemplatePart(Name = PART_CloseButton, Type = typeof(Button))]
[TemplatePart(Name = PART_RecordCountText, Type = typeof(TextBlock))]
public class DbTableViewerDialog : Window
{
    private const string PART_TableComboBox = "PART_TableComboBox";
    private const string PART_DataGrid = "PART_DataGrid";
    private const string PART_CloseButton = "PART_CloseButton";
    private const string PART_RecordCountText = "PART_RecordCountText";

    private ComboBox? _tableComboBox;
    private DataGrid? _dataGrid;
    private TextBlock? _recordCountText;

    private readonly AppDbContext _dbContext;

    public List<TableInfo> Tables { get; } = new()
    {
        new TableInfo("Books", "도서"),
        new TableInfo("Cities", "도시"),
        new TableInfo("BookCities", "도서-도시 매핑")
    };

    static DbTableViewerDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(DbTableViewerDialog),
            new FrameworkPropertyMetadata(typeof(DbTableViewerDialog)));
    }

    public DbTableViewerDialog(AppDbContext dbContext)
    {
        _dbContext = dbContext;
        Title = "DB 테이블 보기";
        Width = 900;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _tableComboBox = GetTemplateChild(PART_TableComboBox) as ComboBox;
        _dataGrid = GetTemplateChild(PART_DataGrid) as DataGrid;
        _recordCountText = GetTemplateChild(PART_RecordCountText) as TextBlock;

        if (GetTemplateChild(PART_CloseButton) is Button closeButton)
            closeButton.Click += (s, e) => Close();

        if (_tableComboBox != null)
        {
            _tableComboBox.ItemsSource = Tables;
            _tableComboBox.DisplayMemberPath = "DisplayName";
            _tableComboBox.SelectionChanged += OnTableSelectionChanged;
            _tableComboBox.SelectedIndex = 0;
        }
    }

    private void OnTableSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_tableComboBox?.SelectedItem is not TableInfo selectedTable || _dataGrid == null)
            return;

        LoadTableData(selectedTable.TableName);
    }

    private void LoadTableData(string tableName)
    {
        if (_dataGrid == null) return;

        try
        {
            object? data = tableName switch
            {
                "Books" => _dbContext.Books.AsNoTracking().ToList(),
                "Cities" => _dbContext.Cities.AsNoTracking().ToList(),
                "BookCities" => _dbContext.BookCities
                    .AsNoTracking()
                    .Include(bc => bc.Book)
                    .Include(bc => bc.City)
                    .Select(bc => new
                    {
                        bc.BookId,
                        BookName = bc.Book.Name,
                        bc.CityId,
                        CityName = bc.City.Name
                    })
                    .ToList(),
                _ => null
            };

            _dataGrid.ItemsSource = data as System.Collections.IEnumerable;

            if (_recordCountText != null && data is System.Collections.ICollection collection)
            {
                _recordCountText.Text = $"총 {collection.Count}개 레코드";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"데이터 로드 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

public class TableInfo
{
    public string TableName { get; }
    public string DisplayName { get; }

    public TableInfo(string tableName, string displayName)
    {
        TableName = tableName;
        DisplayName = displayName;
    }
}
