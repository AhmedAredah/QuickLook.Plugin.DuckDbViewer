@echo off
rem Enables the previews for the current user. Keep this folder: Windows starts the
rem program from here. Run Uninstall.cmd before moving or deleting it.
"%~dp0DuckDbViewer.PreviewHandler.exe" --register
if errorlevel 1 (
    echo Registration failed. The .NET 8 Desktop Runtime is required: https://dotnet.microsoft.com/download/dotnet/8.0
) else (
    echo Done. Data files now preview in File Explorer's preview pane ^(Alt+P^) and in PowerToys Peek.
)
pause
