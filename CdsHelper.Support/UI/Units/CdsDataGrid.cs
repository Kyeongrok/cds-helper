using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace CdsHelper.Support.UI.Units;

public class CdsDataGrid : DataGrid
{
    #region Dependency Properties

    public static readonly DependencyProperty GroupPropertyNameProperty =
        DependencyProperty.Register(nameof(GroupPropertyName), typeof(string), typeof(CdsDataGrid),
            new PropertyMetadata(null, OnGroupPropertyNameChanged));

    public static readonly DependencyProperty GroupPropertyNamesProperty =
        DependencyProperty.Register(nameof(GroupPropertyNames), typeof(string), typeof(CdsDataGrid),
            new PropertyMetadata(null, OnGroupPropertyNamesChanged));

    public static readonly DependencyProperty IsGroupingEnabledProperty =
        DependencyProperty.Register(nameof(IsGroupingEnabled), typeof(bool), typeof(CdsDataGrid),
            new PropertyMetadata(false, OnGroupingEnabledChanged));

    /// <summary>
    /// 그룹핑할 속성 이름 (예: "CulturalSphere") - 단일 그룹핑용
    /// </summary>
    public string? GroupPropertyName
    {
        get => (string?)GetValue(GroupPropertyNameProperty);
        set => SetValue(GroupPropertyNameProperty, value);
    }

    /// <summary>
    /// 다중 그룹핑할 속성 이름들 (쉼표 구분, 예: "CulturalSphere,HasLibrary")
    /// </summary>
    public string? GroupPropertyNames
    {
        get => (string?)GetValue(GroupPropertyNamesProperty);
        set => SetValue(GroupPropertyNamesProperty, value);
    }

    /// <summary>
    /// 그룹핑 활성화 여부
    /// </summary>
    public bool IsGroupingEnabled
    {
        get => (bool)GetValue(IsGroupingEnabledProperty);
        set => SetValue(IsGroupingEnabledProperty, value);
    }

    #endregion

    private ICollectionView? _collectionView;

    static CdsDataGrid()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(CdsDataGrid),
            new FrameworkPropertyMetadata(typeof(CdsDataGrid)));
    }

    public CdsDataGrid()
    {
        AutoGenerateColumns = false;
        IsReadOnly = true;
        CanUserAddRows = false;
        CanUserDeleteRows = false;
        SelectionMode = DataGridSelectionMode.Single;
        SelectionUnit = DataGridSelectionUnit.FullRow;
        GridLinesVisibility = DataGridGridLinesVisibility.All;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 그룹 스타일 적용
        var groupItemStyle = TryFindResource("CdsDataGridGroupItemStyle") as Style;
        if (groupItemStyle != null && GroupStyle.Count == 0)
        {
            var groupStyle = new System.Windows.Controls.GroupStyle
            {
                ContainerStyle = groupItemStyle
            };
            GroupStyle.Add(groupStyle);
        }
    }

    protected override void OnItemsSourceChanged(System.Collections.IEnumerable oldValue, System.Collections.IEnumerable newValue)
    {
        base.OnItemsSourceChanged(oldValue, newValue);
        ApplyGrouping();
    }

    private static void OnGroupPropertyNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CdsDataGrid grid)
            grid.ApplyGrouping();
    }

    private static void OnGroupPropertyNamesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CdsDataGrid grid)
            grid.ApplyGrouping();
    }

    private static void OnGroupingEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CdsDataGrid grid)
            grid.ApplyGrouping();
    }

    /// <summary>
    /// 그룹핑 적용
    /// </summary>
    public void ApplyGrouping()
    {
        if (ItemsSource == null) return;

        _collectionView = CollectionViewSource.GetDefaultView(ItemsSource);
        if (_collectionView == null) return;

        _collectionView.GroupDescriptions.Clear();

        if (!IsGroupingEnabled)
        {
            _collectionView.Refresh();
            return;
        }

        // 다중 그룹핑 (GroupPropertyNames 우선)
        if (!string.IsNullOrEmpty(GroupPropertyNames))
        {
            var propertyNames = GroupPropertyNames.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var propName in propertyNames)
            {
                _collectionView.GroupDescriptions.Add(new PropertyGroupDescription(propName.Trim()));
            }
        }
        // 단일 그룹핑
        else if (!string.IsNullOrEmpty(GroupPropertyName))
        {
            _collectionView.GroupDescriptions.Add(new PropertyGroupDescription(GroupPropertyName));
        }

        _collectionView.Refresh();
    }

    /// <summary>
    /// 단일 그룹핑 설정
    /// </summary>
    public void SetGrouping(string? propertyName)
    {
        GroupPropertyNames = null;
        GroupPropertyName = propertyName;
        IsGroupingEnabled = !string.IsNullOrEmpty(propertyName);
    }

    /// <summary>
    /// 다중 그룹핑 설정
    /// </summary>
    public void SetGrouping(params string[] propertyNames)
    {
        if (propertyNames == null || propertyNames.Length == 0)
        {
            ClearGrouping();
            return;
        }

        GroupPropertyName = null;
        GroupPropertyNames = string.Join(",", propertyNames);
        IsGroupingEnabled = true;
    }

    /// <summary>
    /// 그룹핑 해제
    /// </summary>
    public void ClearGrouping()
    {
        IsGroupingEnabled = false;
        GroupPropertyName = null;
        GroupPropertyNames = null;
    }
}
