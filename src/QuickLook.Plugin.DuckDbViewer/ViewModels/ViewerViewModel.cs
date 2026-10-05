using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickLook.Plugin.DuckDbViewer.Data;
using QuickLook.Plugin.DuckDbViewer.Detection;
using QuickLook.Plugin.DuckDbViewer.Localization;

namespace QuickLook.Plugin.DuckDbViewer.ViewModels;

/// <summary>
/// State and behaviour of the preview panel. All file access runs on background threads;
/// results that arrive after the user moved on (another object, another filter, another page,
/// the preview closed) are cancelled and discarded.
/// </summary>
internal sealed class ViewerViewModel : ObservableObject, IDisposable
{
    public const int PageSize = 500;

    private static readonly IReadOnlyList<ColumnInfo> NoColumns = [];
    private static readonly IReadOnlyList<string?[]> NoRows = [];

    // Nested cancellation scopes, outermost first. Starting a scope cancels the work of the
    // previous scope of that kind and of everything nested inside it.
    private readonly CancellationTokenSource _lifetime = new();   // the whole preview
    private CancellationTokenSource? _objectScope;                // the selected table or view
    private CancellationTokenSource? _queryScope;                 // the current filter and sort
    private CancellationTokenSource? _pageScope;                  // the current page request
    private CancellationTokenSource? _filterEditorScope;          // the value list of the filter popup
    private CancellationTokenSource? _exportScope;                // a running export

    private PreviewDocument? _document;
    private string _path = string.Empty;
    private bool _contentReadyRaised;

    private IReadOnlyList<DataObject> _objects = [];
    private DataObject? _selectedObject;
    private IReadOnlyList<ColumnInfo> _columns = NoColumns;
    private IReadOnlyList<ColumnHeaderViewModel> _columnHeaders = [];
    private IReadOnlyList<SchemaRow> _schemaRows = [];
    private IReadOnlyList<string?[]> _rows = NoRows;
    private RowQuery _query = RowQuery.All;
    private int _pageIndex;
    private long? _totalRows;      // all rows of the object
    private long? _matchingRows;   // rows passing the filters; only tracked while filters exist
    private bool _isLoading;
    private bool _isSchemaVisible;
    private bool _isExporting;
    private string _formatName = string.Empty;
    private string? _noticeTitle;
    private string? _noticeDetail;
    private string? _statusText;
    private string? _exportedPath;

    public ViewerViewModel()
    {
        FirstPageCommand = new DelegateCommand(() => _ = GoToPageAsync(0), () => Page.CanGoPrevious);
        PreviousPageCommand = new DelegateCommand(() => _ = GoToPageAsync(_pageIndex - 1), () => Page.CanGoPrevious);
        NextPageCommand = new DelegateCommand(() => _ = GoToPageAsync(_pageIndex + 1), () => Page.CanGoNext);
        LastPageCommand = new DelegateCommand(() => _ = GoToPageAsync(Page.LastPageIndex ?? 0), () => Page.CanGoLast);

        ClearFiltersCommand = new DelegateCommand(() => _ = ChangeQueryAsync(_query.WithoutFilters()), () => HasFilters);
        CancelExportCommand = new DelegateCommand(() => CancelScope(ref _exportScope), () => IsExporting);
        ShowExportedFileCommand = new DelegateCommand(ShowExportedFile, () => HasExportedFile);
        DismissStatusCommand = new DelegateCommand(ClearStatus, () => !IsExporting);

        ExportActions = Enum.GetValues(typeof(ExportFormat)).Cast<ExportFormat>()
            .Select(format => new ExportAction(
                format,
                Strings.Format("Export_As", format.DisplayName()),
                new DelegateCommand(() => _ = ExportAsync(format), () => CanExport)))
            .ToList();
    }

    /// <summary>Raised once, when there is something to show (content or a notice).</summary>
    public event EventHandler? ContentReady;

    public DelegateCommand FirstPageCommand { get; }

    public DelegateCommand PreviousPageCommand { get; }

    public DelegateCommand NextPageCommand { get; }

    public DelegateCommand LastPageCommand { get; }

    public DelegateCommand ClearFiltersCommand { get; }

    public DelegateCommand CancelExportCommand { get; }

    public DelegateCommand ShowExportedFileCommand { get; }

    public DelegateCommand DismissStatusCommand { get; }

    /// <summary>One entry per export format, for the export menu.</summary>
    public IReadOnlyList<ExportAction> ExportActions { get; }

    // ---- Document and objects --------------------------------------------------------------

    public IReadOnlyList<DataObject> Objects
    {
        get => _objects;
        private set
        {
            if (Set(ref _objects, value))
                OnPropertyChanged(nameof(HasObjectList));
        }
    }

    /// <summary>A single-table file (Parquet) needs no object list.</summary>
    public bool HasObjectList => _objects.Count > 1 || _objects.Any(o => o.IsView);

    public DataObject? SelectedObject
    {
        get => _selectedObject;
        set
        {
            if (Set(ref _selectedObject, value) && value is not null)
                _ = LoadObjectAsync(value);
        }
    }

    public string FormatName
    {
        get => _formatName;
        private set => Set(ref _formatName, value);
    }

    /// <summary>Whether a file is open and has at least one object to browse.</summary>
    public bool HasDocument => _document is not null && _objects.Count > 0;

    /// <summary>Whether the selected object is shown; false while a notice takes its place.</summary>
    public bool HasContent => HasDocument && !HasNotice;

    public bool ShowDataGrid => HasContent && !_isSchemaVisible;

    public bool ShowSchemaGrid => HasContent && _isSchemaVisible;

    // ---- Schema and rows -------------------------------------------------------------------

    public IReadOnlyList<ColumnInfo> Columns
    {
        get => _columns;
        private set
        {
            if (!Set(ref _columns, value))
                return;

            SchemaRows = value.Select((column, index) => new SchemaRow(index + 1, column)).ToList();
            ColumnHeaders = value.Select(column => new ColumnHeaderViewModel(column)).ToList();
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    /// <summary>One per column, in column order.</summary>
    public IReadOnlyList<ColumnHeaderViewModel> ColumnHeaders
    {
        get => _columnHeaders;
        private set => Set(ref _columnHeaders, value);
    }

    public IReadOnlyList<SchemaRow> SchemaRows
    {
        get => _schemaRows;
        private set => Set(ref _schemaRows, value);
    }

    public IReadOnlyList<string?[]> Rows
    {
        get => _rows;
        private set => Set(ref _rows, value);
    }

    /// <summary>The filters and sort order currently applied to <see cref="Rows"/>.</summary>
    public RowQuery Query => _query;

    public bool HasFilters => _query.HasFilters;

    /// <summary>Paging over the rows that pass the filters.</summary>
    public PageState Page => new(PageSize, _pageIndex, _rows.Count, _query.HasFilters ? _matchingRows : _totalRows);

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (Set(ref _isLoading, value))
                OnPropertyChanged(nameof(RangeText));
        }
    }

    public bool IsSchemaVisible
    {
        get => _isSchemaVisible;
        set
        {
            if (!Set(ref _isSchemaVisible, value))
                return;

            OnPropertyChanged(nameof(IsDataVisible));
            OnContentVisibilityChanged();
        }
    }

    public bool IsDataVisible
    {
        get => !_isSchemaVisible;
        set => IsSchemaVisible = !value;
    }

    // ---- Messages --------------------------------------------------------------------------

    /// <summary>A message shown instead of the grid: a failure or an empty database.</summary>
    public string? NoticeTitle
    {
        get => _noticeTitle;
        private set
        {
            if (!Set(ref _noticeTitle, value))
                return;

            OnPropertyChanged(nameof(HasNotice));
            OnContentVisibilityChanged();
        }
    }

    public string? NoticeDetail
    {
        get => _noticeDetail;
        private set => Set(ref _noticeDetail, value);
    }

    public bool HasNotice => _noticeTitle is not null;

    /// <summary>A one-line message above the pager: export progress and results, query errors.</summary>
    public string? StatusText
    {
        get => _statusText;
        private set
        {
            if (Set(ref _statusText, value))
                OnPropertyChanged(nameof(HasStatus));
        }
    }

    public bool HasStatus => _statusText is not null;

    public bool IsExporting
    {
        get => _isExporting;
        private set
        {
            if (!Set(ref _isExporting, value))
                return;

            OnPropertyChanged(nameof(CanDismissStatus));
            CancelExportCommand.RaiseCanExecuteChanged();
            DismissStatusCommand.RaiseCanExecuteChanged();
            RaiseCanExportChanged();
        }
    }

    public bool CanDismissStatus => !_isExporting;

    public bool CanExport => HasContent && !_isExporting && _columns.Count > 0;

    /// <summary>The file written by the last successful export.</summary>
    public string? ExportedPath
    {
        get => _exportedPath;
        private set
        {
            if (!Set(ref _exportedPath, value))
                return;

            OnPropertyChanged(nameof(HasExportedFile));
            ShowExportedFileCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasExportedFile => _exportedPath is not null;

    public string SummaryText
    {
        get
        {
            var columns = _columns.Count.ToString("N0");
            if (_query.HasFilters && _matchingRows is { } matching && _totalRows is { } all)
                return Strings.Format("Summary_Filtered", matching.ToString("N0"), all.ToString("N0"), columns);

            return Page.TotalRows is { } total
                ? Strings.Format("Summary_RowsAndColumns", total.ToString("N0"), columns)
                : Strings.Format("Summary_Columns", columns);
        }
    }

    public string RangeText
    {
        get
        {
            var page = Page;
            if (page.RowsOnPage == 0)
                return _isLoading ? string.Empty : Strings.Get("Range_Empty");

            var first = page.FirstRow.ToString("N0");
            var last = page.LastRow.ToString("N0");
            return page.TotalRows is { } total
                ? Strings.Format("Range_Known", first, last, total.ToString("N0"))
                : Strings.Format("Range_Unknown", first, last);
        }
    }

    public string PageText
    {
        get
        {
            var page = Page;
            var current = (page.PageIndex + 1).ToString("N0");
            return page.PageCount is { } count && count > 0
                ? Strings.Format("Pager_PageOf", current, count.ToString("N0"))
                : Strings.Format("Pager_Page", current);
        }
    }

    // ---- Opening and browsing --------------------------------------------------------------

    /// <summary>Opens the file and shows its first object.</summary>
    public async Task OpenAsync(string path)
    {
        var token = _lifetime.Token;
        _path = path;
        IsLoading = true;

        try
        {
            var document = await Task.Run(() => PreviewDocument.Open(path, token), token);
            if (token.IsCancellationRequested)
            {
                document.Dispose();
                return;
            }

            _document = document;
            FormatName = DescribeFormat(document.Format);
            Objects = document.Objects;
            OnPropertyChanged(nameof(HasDocument));
            OnContentVisibilityChanged();

            if (document.Objects.Count == 0)
            {
                ShowNotice(Strings.Get("Notice_NoObjects"), null);
                return;
            }

            // Assign the field directly so the load can be awaited here.
            _selectedObject = document.Objects[0];
            OnPropertyChanged(nameof(SelectedObject));
            await LoadObjectAsync(document.Objects[0]);
        }
        catch (OperationCanceledException)
        {
            // The preview was closed while opening.
        }
        catch (Exception e)
        {
            ShowNotice(Strings.Get("Notice_OpenFailed"), DescribeError(e));
        }
    }

    private async Task LoadObjectAsync(DataObject source)
    {
        if (_document is not { } document)
            return;

        var token = BeginScope(ref _objectScope, _lifetime.Token);
        CancelScope(ref _queryScope);
        CancelScope(ref _pageScope);
        CancelScope(ref _filterEditorScope);

        NoticeTitle = null;
        NoticeDetail = null;
        _query = RowQuery.All;
        _totalRows = null;
        _matchingRows = null;
        Rows = NoRows;
        Columns = NoColumns;
        IsLoading = true;
        OnRowsChanged(pageIndex: 0);
        OnQueryChanged();

        try
        {
            var (columns, rows) = await Task.Run(
                () => (document.GetColumns(source, token),
                       document.ReadPage(source, RowQuery.All, 0, PageSize, token)),
                token);
            if (token.IsCancellationRequested)
                return;

            Columns = columns;
            Rows = rows;
            IsLoading = false;
            OnRowsChanged(pageIndex: 0);
            RaiseContentReady();

            // Counting can take a while on large views; the first page is already visible.
            var total = await Task.Run(() => document.CountRows(source, RowQuery.All, token), token);
            if (token.IsCancellationRequested)
                return;

            _totalRows = total;
            OnRowsChanged(_pageIndex);
        }
        catch (OperationCanceledException)
        {
            // Superseded by another selection or the preview was closed.
        }
        catch (Exception e) when (!token.IsCancellationRequested)
        {
            ShowReadFailure(e);
        }
    }

    public async Task GoToPageAsync(int pageIndex)
    {
        if (_document is not { } document || _selectedObject is not { } source || _objectScope is null)
            return;

        var token = BeginScope(ref _pageScope, (_queryScope ?? _objectScope).Token);
        var query = _query;
        IsLoading = true;

        try
        {
            var rows = await Task.Run(
                () => document.ReadPage(source, query, (long)pageIndex * PageSize, PageSize, token),
                token);
            if (token.IsCancellationRequested)
                return;

            Rows = rows;
            IsLoading = false;
            OnRowsChanged(pageIndex);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer page request.
        }
        catch (Exception e) when (!token.IsCancellationRequested)
        {
            ShowReadFailure(e);
        }
    }

    // ---- Sorting and filtering -------------------------------------------------------------

    /// <summary>Cycles a column through ascending, descending and unsorted.</summary>
    public Task ToggleSortAsync(ColumnHeaderViewModel header)
    {
        var current = _query.Sort is { } sort && sort.Column.Name == header.Name ? sort : null;
        SortOrder? next = current switch
        {
            null => new SortOrder(header.Column, descending: false),
            { Descending: false } => new SortOrder(header.Column, descending: true),
            _ => null,
        };

        return ChangeQueryAsync(_query.WithSort(next));
    }

    /// <summary>Starts editing the filter of a column; its values are listed in the background.</summary>
    public FilterEditorViewModel BeginFilter(ColumnHeaderViewModel header)
    {
        var editor = new FilterEditorViewModel(header.Column, _query.FilterOn(header.Name));
        if (_document is { } document && _selectedObject is { } source && _objectScope is not null)
            _ = LoadFilterValuesAsync(editor, document, source);

        return editor;
    }

    private async Task LoadFilterValuesAsync(FilterEditorViewModel editor, PreviewDocument document, DataObject source)
    {
        var token = BeginScope(ref _filterEditorScope, _objectScope!.Token);
        var query = _query;

        try
        {
            var values = await Task.Run(
                () => document.GetDistinctValues(source, editor.Column.Name, query, token), token);
            if (!token.IsCancellationRequested)
                editor.Load(values);
        }
        catch (OperationCanceledException)
        {
            // The popup was closed or another one was opened.
        }
        catch (Exception e) when (!token.IsCancellationRequested)
        {
            editor.Fail(DescribeError(e));
        }
    }

    /// <summary>Stops listing values for a filter popup that was closed.</summary>
    public void EndFilter() => CancelScope(ref _filterEditorScope);

    public Task ApplyFilterAsync(FilterEditorViewModel editor)
    {
        var filter = editor.BuildFilter();
        return ChangeQueryAsync(filter is null
            ? _query.WithoutFilter(editor.Column.Name)
            : _query.WithFilter(filter));
    }

    public Task ClearFilterAsync(string column) => ChangeQueryAsync(_query.WithoutFilter(column));

    /// <summary>
    /// Applies a new filter and sort order and shows its first page. If the engine rejects it
    /// (some types cannot be ordered, for instance) the previous query stays in effect.
    /// </summary>
    private async Task ChangeQueryAsync(RowQuery query)
    {
        if (_document is not { } document || _selectedObject is not { } source || _objectScope is null)
            return;

        var token = BeginScope(ref _queryScope, _objectScope.Token);
        CancelScope(ref _pageScope);

        var previous = _query;
        var previousMatching = _matchingRows;
        _query = query;
        _matchingRows = null;
        IsLoading = true;
        OnQueryChanged();

        try
        {
            var rows = await Task.Run(() => document.ReadPage(source, query, 0, PageSize, token), token);
            if (token.IsCancellationRequested)
                return;

            Rows = rows;
            IsLoading = false;
            OnRowsChanged(pageIndex: 0);

            if (!query.HasFilters)
                return;

            var matching = await Task.Run(() => document.CountRows(source, query, token), token);
            if (token.IsCancellationRequested)
                return;

            _matchingRows = matching;
            OnRowsChanged(_pageIndex);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer query or another selection.
        }
        catch (Exception e) when (!token.IsCancellationRequested)
        {
            _query = previous;
            _matchingRows = previousMatching;
            IsLoading = false;
            OnQueryChanged();
            OnRowsChanged(_pageIndex);
            ShowStatus(DescribeError(e));
        }
    }

    // ---- Export ----------------------------------------------------------------------------

    /// <summary>
    /// Writes the selected object, with the current filters and sort order, to a new file next
    /// to the previewed one.
    /// </summary>
    /// <remarks>
    /// There is deliberately no save dialog: QuickLook's global hotkeys treat Enter and Space
    /// as "open" and "close preview" while any of its windows has the focus, including a
    /// dialog, so confirming a file name would close the preview.
    /// </remarks>
    public async Task ExportAsync(ExportFormat format)
    {
        if (!CanExport || _document is not { } document || _selectedObject is not { } source)
            return;

        var token = BeginScope(ref _exportScope, _lifetime.Token);
        var query = _query;
        string target;
        try
        {
            target = ExportTarget.Choose(_path, HasObjectList ? source.Name : null, format);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowStatus(Strings.Format("Export_Failed", e.Message));
            return;
        }

        ExportedPath = null;
        IsExporting = true;
        StatusText = Strings.Format("Export_Running", Path.GetFileName(target));

        try
        {
            await Task.Run(() => document.Export(source, query, target, format, token), token);
            ExportedPath = target;
            StatusText = Strings.Format("Export_Done", Path.GetFileName(target));
        }
        catch (OperationCanceledException)
        {
            StatusText = null;
        }
        catch (Exception e)
        {
            StatusText = Strings.Format("Export_Failed", DescribeError(e));
        }
        finally
        {
            IsExporting = false;
        }
    }

    private void ShowExportedFile()
    {
        if (_exportedPath is not { } path || !File.Exists(path))
            return;

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    private void ShowStatus(string text)
    {
        ExportedPath = null;
        StatusText = text;
    }

    private void ClearStatus()
    {
        ExportedPath = null;
        StatusText = null;
    }

    // ---- Helpers ---------------------------------------------------------------------------

    /// <summary>Publishes everything derived from the rows, the page position and the counts.</summary>
    private void OnRowsChanged(int pageIndex)
    {
        _pageIndex = pageIndex;
        OnPropertyChanged(nameof(Page));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(RangeText));
        OnPropertyChanged(nameof(PageText));
        FirstPageCommand.RaiseCanExecuteChanged();
        PreviousPageCommand.RaiseCanExecuteChanged();
        NextPageCommand.RaiseCanExecuteChanged();
        LastPageCommand.RaiseCanExecuteChanged();
        RaiseCanExportChanged();
    }

    private void OnQueryChanged()
    {
        foreach (var header in _columnHeaders)
            header.Reflect(_query);

        OnPropertyChanged(nameof(Query));
        OnPropertyChanged(nameof(HasFilters));
        OnPropertyChanged(nameof(SummaryText));
        ClearFiltersCommand.RaiseCanExecuteChanged();
    }

    private void OnContentVisibilityChanged()
    {
        OnPropertyChanged(nameof(HasContent));
        OnPropertyChanged(nameof(ShowDataGrid));
        OnPropertyChanged(nameof(ShowSchemaGrid));
        RaiseCanExportChanged();
    }

    private void RaiseCanExportChanged()
    {
        OnPropertyChanged(nameof(CanExport));
        foreach (var action in ExportActions)
            action.Command.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// A file that is a single table (Parquet) has nothing else to offer when that table
    /// cannot be read, so it is reported as a failure of the file itself.
    /// </summary>
    private void ShowReadFailure(Exception error)
    {
        var title = HasObjectList ? "Notice_ReadFailed" : "Notice_OpenFailed";
        ShowNotice(Strings.Get(title), DescribeError(error));
    }

    /// <summary>
    /// DuckDB appends the failing statement to its messages ("LINE 1: SELECT ..."); that part
    /// is the plugin's own SQL and means nothing to the user.
    /// </summary>
    internal static string DescribeError(Exception error)
    {
        var message = error.Message.Replace("\r\n", "\n");
        var statement = message.IndexOf("\nLINE ", StringComparison.Ordinal);
        if (statement >= 0)
            message = message.Substring(0, statement);

        return message.Trim();
    }

    private void ShowNotice(string title, string? detail)
    {
        IsLoading = false;
        NoticeDetail = detail;
        NoticeTitle = title;
        RaiseContentReady();
    }

    private void RaiseContentReady()
    {
        if (_contentReadyRaised)
            return;

        _contentReadyRaised = true;
        ContentReady?.Invoke(this, EventArgs.Empty);
    }

    private static string DescribeFormat(FileFormat format) => format switch
    {
        FileFormat.Parquet => "Parquet",
        FileFormat.DuckDb => "DuckDB",
        FileFormat.Sqlite => "SQLite",
        FileFormat.Avro => "Avro",
        FileFormat.Arrow => "Arrow",
        _ => string.Empty,
    };

    /// <summary>Cancels whatever ran in <paramref name="scope"/> and starts a fresh one.</summary>
    private static CancellationToken BeginScope(ref CancellationTokenSource? scope, CancellationToken parent)
    {
        CancelScope(ref scope);
        scope = CancellationTokenSource.CreateLinkedTokenSource(parent);
        return scope.Token;
    }

    private static void CancelScope(ref CancellationTokenSource? scope)
    {
        scope?.Cancel();
        scope?.Dispose();
        scope = null;
    }

    public void Dispose()
    {
        if (_lifetime.IsCancellationRequested)
            return;

        // Cancelling interrupts a running query or export; closing the engine then releases
        // the file. Done off the UI thread because it waits for that statement to stop.
        _lifetime.Cancel();
        var document = _document;
        _document = null;
        if (document is not null)
            Task.Run(document.Dispose);
    }
}
