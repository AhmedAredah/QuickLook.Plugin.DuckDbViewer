@echo off
rem Removes the previews for the current user and stops the running preview process.
"%~dp0DuckDbViewer.PreviewHandler.exe" --unregister
taskkill /im DuckDbViewer.PreviewHandler.exe /f >nul 2>&1
echo Done. This folder can now be deleted.
pause
