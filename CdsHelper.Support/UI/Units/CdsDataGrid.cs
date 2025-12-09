using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CdsHelper.Support.UI.Units;

public class CdsDataGrid : DataGrid
{
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
    }
}
