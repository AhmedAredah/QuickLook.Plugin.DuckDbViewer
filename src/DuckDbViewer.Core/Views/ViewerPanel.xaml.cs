using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using DuckDbViewer.ViewModels;

namespace DuckDbViewer.Views;

/// <summary>
/// The preview panel. Everything is data-bound except what depends on the previewed object's
/// schema (the data grid's columns) and the opening and closing of popups.
/// </summary>
internal partial class ViewerPanel : UserControl, IDisposable
{
    private const string NullText = "NULL";
    private const double MaxColumnWidth = 420;
    private const double MinColumnWidth = 44;

    private readonly ViewerViewModel _viewModel = new();

    public ViewerPanel()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        exportPopup.CustomPopupPlacementCallback = PlaceBelowRightAligned;
    }

    /// <summary>
    /// Opens a popup below its button with the right edges aligned. The export button sits at
    /// the panel's right edge, so a left-aligned popup would hang outside the window.
    /// </summary>
    private static CustomPopupPlacement[] PlaceBelowRightAligned(Size popupSize, Size targetSize, Point offset)
    {
        return [new CustomPopupPlacement(new Point(targetSize.Width - popupSize.Width, targetSize.Height), PopupPrimaryAxis.Horizontal)];
    }

    public ViewerViewModel ViewModel => _viewModel;

    /// <summary>Starts loading <paramref name="path"/>.</summary>
    /// <param name="onReady">Called once when the first content, or a failure message, is visible.</param>
    /// <returns>A task that completes when the first object is fully loaded, row count included.</returns>
    public Task Open(string path, Action onReady)
    {
        _viewModel.ContentReady += (_, _) => onReady();
        return _viewModel.OpenAsync(path);
    }

    /// <summary>Opens the filter popup of a column below <paramref name="anchor"/>.</summary>
    public void ShowFilter(ColumnHeaderViewModel header, UIElement anchor)
    {
        filterPopup.DataContext = _viewModel.BeginFilter(header);
        filterPopup.PlacementTarget = anchor;
        filterPopup.IsOpen = true;
    }

    public void ShowExportMenu() => exportPopup.IsOpen = true;

    public void Dispose()
    {
        filterPopup.IsOpen = false;
        exportPopup.IsOpen = false;
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.Dispose();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ViewerViewModel.ColumnHeaders):
                RebuildColumns();
                break;
            case nameof(ViewerViewModel.Rows):
                ScrollToTop();
                break;
        }
    }

    private void RebuildColumns()
    {
        var headerTemplate = (DataTemplate)FindResource("ColumnHeaderTemplate");
        var numericHeaderStyle = (Style)FindResource("Viewer.NumericColumnHeader");

        dataGrid.Columns.Clear();
        for (var i = 0; i < _viewModel.ColumnHeaders.Count; i++)
        {
            var header = _viewModel.ColumnHeaders[i];
            var isNumeric = header.Column.IsNumeric;

            // Rows are string arrays, so binding by position works for any column name.
            var column = new DataGridTextColumn
            {
                Header = header,
                HeaderTemplate = headerTemplate,
                Binding = new Binding($"[{i}]") { Mode = BindingMode.OneTime, TargetNullValue = NullText },
                ElementStyle = CreateCellTextStyle(i, isNumeric),
                MinWidth = MinColumnWidth,
                MaxWidth = MaxColumnWidth,
            };
            if (isNumeric)
                column.HeaderStyle = numericHeaderStyle;

            dataGrid.Columns.Add(column);
        }
    }

    /// <summary>The shared cell style plus a muted, italic look for SQL NULLs.</summary>
    private Style CreateCellTextStyle(int columnIndex, bool isNumeric)
    {
        var baseStyle = (Style)FindResource(isNumeric ? "Viewer.NumericCellText" : "Viewer.CellText");
        var style = new Style(typeof(TextBlock), baseStyle);

        var isNull = new DataTrigger { Binding = new Binding($"[{columnIndex}]"), Value = null };
        isNull.Setters.Add(new Setter(TextBlock.ForegroundProperty, FindResource("Viewer.Muted")));
        isNull.Setters.Add(new Setter(TextBlock.FontStyleProperty, FontStyles.Italic));
        style.Triggers.Add(isNull);

        style.Seal();
        return style;
    }

    private void ScrollToTop()
    {
        if (_viewModel.Rows.Count > 0)
            dataGrid.ScrollIntoView(_viewModel.Rows[0]);
    }

    private void DataGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        var rowNumber = _viewModel.Page.Offset + e.Row.GetIndex() + 1;
        e.Row.Header = rowNumber.ToString("N0");
    }

    /// <summary>
    /// A header click sorts the whole table in the engine. The grid's own sorting, which would
    /// only reorder the rows of the visible page, is suppressed.
    /// </summary>
    private void DataGrid_Sorting(object? sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (e.Column.Header is ColumnHeaderViewModel header)
            _ = _viewModel.ToggleSortAsync(header);
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        var button = (FrameworkElement)sender;
        if (button.DataContext is ColumnHeaderViewModel header)
            ShowFilter(header, button);

        e.Handled = true;
    }

    private void ApplyFilter_Click(object sender, RoutedEventArgs e)
    {
        if (filterPopup.DataContext is FilterEditorViewModel editor)
            _ = _viewModel.ApplyFilterAsync(editor);

        filterPopup.IsOpen = false;
    }

    private void RemoveFilter_Click(object sender, RoutedEventArgs e)
    {
        if (filterPopup.DataContext is FilterEditorViewModel editor)
            _ = _viewModel.ClearFilterAsync(editor.Column.Name);

        filterPopup.IsOpen = false;
    }

    private void FilterPopup_Closed(object? sender, EventArgs e) => _viewModel.EndFilter();

    private void ExportButton_Click(object sender, RoutedEventArgs e) => ShowExportMenu();

    private void ExportAction_Click(object sender, RoutedEventArgs e) => exportPopup.IsOpen = false;
}
