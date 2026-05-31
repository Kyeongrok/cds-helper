using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CdsHelper.Main.Local.ViewModels;

namespace CdsHelper.Main.UI.Views;

public class ExePatchContent : ContentControl
{
    private DataGrid? _customPatchGrid;

    static ExePatchContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ExePatchContent),
            new FrameworkPropertyMetadata(typeof(ExePatchContent)));
    }

    public ExePatchContent()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ExePatchContentViewModel)
            DataContext = new ExePatchContentViewModel();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_customPatchGrid != null)
            _customPatchGrid.SelectionChanged -= OnCustomPatchSelectionChanged;

        _customPatchGrid = GetTemplateChild("PART_CustomPatchGrid") as DataGrid;

        if (_customPatchGrid != null)
            _customPatchGrid.SelectionChanged += OnCustomPatchSelectionChanged;
    }

    /// <summary>
    /// 커스텀 패치 행을 선택하면 "값" 칸에 자동으로 커서를 넣고 전체 선택한다.
    /// 단, 이름/주소 등 다른 입력 칸을 직접 클릭한 경우엔 그쪽 편집을 방해하지 않는다.
    /// </summary>
    private void OnCustomPatchSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_customPatchGrid?.SelectedItem == null) return;

        // 사용자가 특정 입력 컨트롤을 직접 클릭했으면 포커스를 가로채지 않는다
        if (Keyboard.FocusedElement is TextBox or ComboBox or
            System.Windows.Controls.Primitives.ToggleButton)
            return;

        var item = _customPatchGrid.SelectedItem;
        var grid = _customPatchGrid;

        // 행 컨테이너가 생성된 뒤(레이아웃 후) 포커스를 줘야 한다
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (grid.ItemContainerGenerator.ContainerFromItem(item) is not DataGridRow row) return;
            var tb = FindTaggedTextBox(row, "ValueCell");
            if (tb != null)
            {
                tb.Focus();
                tb.SelectAll();
            }
        }), DispatcherPriority.Input);
    }

    private static TextBox? FindTaggedTextBox(DependencyObject parent, string tag)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBox tb && (tb.Tag as string) == tag)
                return tb;

            var found = FindTaggedTextBox(child, tag);
            if (found != null) return found;
        }
        return null;
    }
}
