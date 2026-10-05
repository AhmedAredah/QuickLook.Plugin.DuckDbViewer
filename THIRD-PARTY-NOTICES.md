# Third-party notices

The plugin package (`.qlplugin`) redistributes the following components. Their licence
texts are included in the package under `licenses\` where the upstream package ships one.

| Component | Use | Licence |
|---|---|---|
| [DuckDB](https://github.com/duckdb/duckdb) (`duckdb.dll`) | Query engine | MIT |
| [DuckDB.NET](https://github.com/Giorgi/DuckDB.NET) (`DuckDB.NET.Data.dll`, `DuckDB.NET.Bindings.dll`) | .NET bindings for DuckDB | MIT |
| [sqlite_scanner](https://github.com/duckdb/duckdb-sqlite) (`extensions\sqlite_scanner.duckdb_extension`) | DuckDB extension for reading SQLite files | MIT |
| [avro](https://github.com/duckdb/duckdb-avro) (`extensions\avro.duckdb_extension`) | DuckDB extension for reading Avro files | MIT |
| [nanoarrow](https://github.com/duckdb/duckdb-nanoarrow) (`extensions\nanoarrow.duckdb_extension`) | DuckDB community extension for reading Arrow IPC files | MIT |
| `System.Memory`, `System.Buffers`, `System.Numerics.Vectors`, `System.Runtime.CompilerServices.Unsafe` | .NET support libraries required by DuckDB.NET | MIT |

The plugin is compiled against [QuickLook.Common](https://github.com/QL-Win/QuickLook),
which is provided by QuickLook at runtime and is not redistributed.
