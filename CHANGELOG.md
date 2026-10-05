# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.1.0]

### Added

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
