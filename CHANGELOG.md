# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.2.0] - 2026-10-05

### Added

- SQLite-based files with other extensions: GeoPackage (`.gpkg`), MBTiles (`.mbtiles`),
  `.s3db`, `.sl3` and `.sqlitedb`.
- Preview of Avro files (`.avro`).
- Preview of Arrow IPC files and streams and Feather version 2 files (`.arrow`, `.arrows`,
  `.feather`, `.ipc`), uncompressed or ZSTD-compressed.

### Changed

- The full package now bundles the Avro and Arrow extensions as well and is about 55 MB.
  The lite package contains no extensions and downloads each on first use.

## [0.1.0] - 2026-10-05

### Added

- Sorting by any column, applied to the whole table.
- Filtering by column values, with value counts and combinable filters.
- Export of the current view to CSV, Parquet or JSON, from the panel and from QuickLook's
  `⋯` menu.
- Preview of Parquet files (`.parquet`, `.parq`).
- Preview of DuckDB databases (`.duckdb`, `.ddb`, `.db`), including views and objects in
  schemas other than `main`.
- Preview of SQLite databases (`.sqlite`, `.sqlite3`, `.db3`, `.db`), including rows whose
  values do not match the declared column type.
- Paged data grid (500 rows per page) with row numbers, NULL highlighting and
  right-aligned numeric columns.
- Schema tab listing column names, types and nullability.
- Light and dark theme support following QuickLook.
- English and Arabic user interface text.
