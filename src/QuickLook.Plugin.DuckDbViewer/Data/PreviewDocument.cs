using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using QuickLook.Plugin.DuckDbViewer.Detection;
using QuickLook.Plugin.DuckDbViewer.Formats;

namespace QuickLook.Plugin.DuckDbViewer.Data;

/// <summary>
/// An opened file and the objects inside it. This is the only type the UI layer talks to.
/// All members block and are meant to be called from a background thread.
/// </summary>
internal sealed class PreviewDocument : IDisposable
{
    private readonly DuckSession _session;
    private readonly FormatHandler _handler;

    private PreviewDocument(DuckSession session, FormatHandler handler, FileFormat format,
        IReadOnlyList<DataObject> objects)
    {
        _session = session;
        _handler = handler;
        Format = format;
        Objects = objects;
    }

    public FileFormat Format { get; }

    public IReadOnlyList<DataObject> Objects { get; }

    public static PreviewDocument Open(string path, CancellationToken cancellationToken = default)
    {
        var format = FormatDetector.Detect(path);
        if (format == FileFormat.Unknown)
            throw new InvalidDataException($"'{Path.GetFileName(path)}' is not a supported data file.");

        var handler = FormatHandler.For(format);
        var session = new DuckSession();
        try
        {
            var objects = handler.Open(session, path, cancellationToken);
            return new PreviewDocument(session, handler, format, objects);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public IReadOnlyList<ColumnInfo> GetColumns(DataObject source, CancellationToken cancellationToken = default)
    {
        return _handler.DescribeColumns(_session, source, cancellationToken);
    }

    public long CountRows(DataObject source, CancellationToken cancellationToken = default)
    {
        return TableReader.CountRows(_session, source, cancellationToken);
    }

    public IReadOnlyList<string?[]> ReadPage(DataObject source, long offset, int limit,
        CancellationToken cancellationToken = default)
    {
        return TableReader.ReadPage(_session, source, offset, limit, cancellationToken);
    }

    /// <summary>Closes the engine and releases the handle on the previewed file.</summary>
    public void Dispose() => _session.Dispose();
}
