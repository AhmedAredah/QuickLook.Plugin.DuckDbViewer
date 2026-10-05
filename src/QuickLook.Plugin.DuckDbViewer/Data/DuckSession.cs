using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using DuckDB.NET.Data;

namespace QuickLook.Plugin.DuckDbViewer.Data;

/// <summary>
/// An in-memory DuckDB instance used to read one previewed file.
/// </summary>
/// <remarks>
/// A DuckDB connection runs one statement at a time, so calls are serialized. Every call
/// takes a <see cref="CancellationToken"/>: a statement that is waiting is skipped and a
/// statement that is running is interrupted when the token is cancelled.
/// </remarks>
internal sealed class DuckSession : IDisposable
{
    private const int MaxThreads = 4;
    private const string MemoryLimit = "2GB";

    private readonly object _gate = new();
    private readonly DuckDBConnection _connection;
    private bool _disposed;

    public DuckSession()
    {
        _connection = new DuckDBConnection("DataSource=:memory:");
        _connection.Open();

        try
        {
            // A previewer must be predictable: never download anything behind the user's
            // back and stay modest with CPU and memory.
            Execute("SET autoinstall_known_extensions = false");
            Execute("SET autoload_known_extensions = false");
            Execute($"SET threads = {Math.Min(MaxThreads, Environment.ProcessorCount)}");
            Execute($"SET memory_limit = {SqlText.Literal(MemoryLimit)}");
        }
        catch
        {
            _connection.Dispose();
            throw;
        }
    }

    public void Execute(string sql, CancellationToken cancellationToken = default)
    {
        Run(sql, cancellationToken, command => command.ExecuteNonQuery());
    }

    public object? Scalar(string sql, CancellationToken cancellationToken = default)
    {
        return Run(sql, cancellationToken, command => command.ExecuteScalar());
    }

    public List<T> Query<T>(string sql, Func<IDataRecord, T> map, CancellationToken cancellationToken = default)
    {
        return Run(sql, cancellationToken, command =>
        {
            var rows = new List<T>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                rows.Add(map(reader));
            }

            return rows;
        });
    }

    /// <summary>
    /// Runs several statements as one unit, so no other caller's statement can slip in between
    /// (for example while a setting is temporarily changed).
    /// </summary>
    public T Exclusive<T>(Func<T> body)
    {
        lock (_gate)
            return body();
    }

    private T Run<T>(string sql, CancellationToken cancellationToken, Func<DuckDBCommand, T> body)
    {
        lock (_gate)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DuckSession));

            cancellationToken.ThrowIfCancellationRequested();

            using var command = _connection.CreateCommand();
            command.CommandText = sql;

            using (cancellationToken.Register(() => Interrupt(command)))
            {
                try
                {
                    return body(command);
                }
                catch (DuckDBException) when (cancellationToken.IsCancellationRequested)
                {
                    // DuckDB reports an interrupted statement as an ordinary error.
                    throw new OperationCanceledException(cancellationToken);
                }
            }
        }
    }

    private static void Interrupt(DuckDBCommand command)
    {
        try
        {
            command.Cancel();
        }
        catch (Exception e) when (e is DuckDBException or InvalidOperationException or ObjectDisposedException)
        {
            // The statement finished on its own; nothing left to interrupt.
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _connection.Dispose();
        }
    }
}
