using System;
using System.Collections.Generic;
using System.Linq;
using DuckDbViewer.Data;
using DuckDbViewer.Localization;

namespace DuckDbViewer.ViewModels;

/// <summary>One value in the filter checklist.</summary>
internal sealed class FilterValueItem : ObservableObject
{
    private const int MaxDisplayLength = 80;

    private readonly Action _changed;
    private bool _isChecked;

    public FilterValueItem(string? value, long? count, bool isChecked, Action changed)
    {
        Value = value;
        CountText = count?.ToString("N0") ?? string.Empty;
        _isChecked = isChecked;
        _changed = changed;
    }

    public string? Value { get; }

    public bool IsNull => Value is null;

    /// <summary>A single-line, shortened rendering of the value.</summary>
    public string Display
    {
        get
        {
            if (Value is null)
                return "NULL";

            var line = Value.Replace('\r', ' ').Replace('\n', ' ');
            return line.Length > MaxDisplayLength ? line.Substring(0, MaxDisplayLength) + "…" : line;
        }
    }

    public string CountText { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (Set(ref _isChecked, value))
                _changed();
        }
    }
}

/// <summary>
/// The filter popup of one column: a checklist of the column's most frequent values.
/// </summary>
/// <remarks>
/// The list may be incomplete for columns with very many values. Unticking values of the full
/// list therefore means "everything except these" (unlisted values stay visible), while
/// starting from an empty selection means "only these".
/// </remarks>
internal sealed class FilterEditorViewModel : ObservableObject
{
    private readonly ColumnFilter? _existing;
    private IReadOnlyList<FilterValueItem> _items = [];
    private bool _isLoading = true;
    private bool _isTruncated;
    private bool _isExclusion;
    private string? _error;

    public FilterEditorViewModel(ColumnInfo column, ColumnFilter? existing)
    {
        Column = column;
        _existing = existing;
        _isExclusion = existing?.IsExclusion ?? true;

        SelectAllCommand = new DelegateCommand(() => SetAll(true), () => !IsLoading);
        SelectNoneCommand = new DelegateCommand(() => SetAll(false), () => !IsLoading);
    }

    public ColumnInfo Column { get; }

    public string Title => Column.Name;

    public DelegateCommand SelectAllCommand { get; }

    public DelegateCommand SelectNoneCommand { get; }

    public IReadOnlyList<FilterValueItem> Items
    {
        get => _items;
        private set => Set(ref _items, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!Set(ref _isLoading, value))
                return;

            OnPropertyChanged(nameof(CanApply));
            SelectAllCommand.RaiseCanExecuteChanged();
            SelectNoneCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsTruncated
    {
        get => _isTruncated;
        private set => Set(ref _isTruncated, value);
    }

    public string TruncatedText => Strings.Format("Filter_Truncated", TableReader.MaxDistinctValues.ToString("N0"));

    /// <summary>Set when the values could not be listed.</summary>
    public string? Error
    {
        get => _error;
        private set
        {
            if (Set(ref _error, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => _error is not null;

    /// <summary>A filter that lets nothing through is never useful.</summary>
    public bool CanApply => !IsLoading && !HasError && _items.Any(i => i.IsChecked);

    /// <summary>Whether a filter is currently set on the column, so it can be removed.</summary>
    public bool HasExistingFilter => _existing is not null;

    public void Load(ColumnValues values)
    {
        var items = values.Values
            .Select(v => new FilterValueItem(v.Value, v.Count, _existing?.Accepts(v.Value) ?? true, OnItemChanged))
            .ToList();

        // Values of the current filter that are not in the list (rare or hidden by other
        // filters) are appended so that editing the filter does not silently drop them.
        if (_existing is not null)
        {
            var listed = new HashSet<string?>(values.Values.Select(v => v.Value), StringComparer.Ordinal);
            items.AddRange(_existing.Values
                .Where(v => !listed.Contains(v))
                .Select(v => new FilterValueItem(v, null, !_existing.IsExclusion, OnItemChanged)));
        }

        Items = items;
        IsTruncated = values.IsTruncated;
        IsLoading = false;
    }

    public void Fail(string message)
    {
        Error = message;
        IsLoading = false;
    }

    /// <summary>The filter described by the checklist, or <c>null</c> when every row passes.</summary>
    public ColumnFilter? BuildFilter()
    {
        if (_isExclusion)
        {
            var dropped = _items.Where(i => !i.IsChecked).Select(i => i.Value).ToList();
            return dropped.Count == 0 ? null : ColumnFilter.Exclude(Column.Name, dropped);
        }

        var kept = _items.Where(i => i.IsChecked).Select(i => i.Value).ToList();
        return !_isTruncated && kept.Count == _items.Count ? null : ColumnFilter.Include(Column.Name, kept);
    }

    private void SetAll(bool isChecked)
    {
        foreach (var item in _items)
            item.IsChecked = isChecked;

        // "All, then untick" excludes; "none, then tick" includes.
        _isExclusion = isChecked;
        OnPropertyChanged(nameof(CanApply));
    }

    private void OnItemChanged() => OnPropertyChanged(nameof(CanApply));
}
