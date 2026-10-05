using DuckDbViewer.Data;

namespace DuckDbViewer.ViewModels;

/// <summary>A menu entry that exports the current view in one format.</summary>
internal sealed class ExportAction
{
    public ExportAction(ExportFormat format, string title, DelegateCommand command)
    {
        Format = format;
        Title = title;
        Command = command;
    }

    public ExportFormat Format { get; }

    public string Title { get; }

    public DelegateCommand Command { get; }
}
