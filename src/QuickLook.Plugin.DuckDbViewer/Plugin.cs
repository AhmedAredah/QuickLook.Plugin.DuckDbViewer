using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using QuickLook.Common.Controls;
using QuickLook.Common.Helpers;
using QuickLook.Common.Plugin;
using QuickLook.Common.Plugin.MoreMenu;
using DuckDbViewer.Detection;
using DuckDbViewer.Views;
using QuickLook.Plugin.DuckDbViewer.Native;

namespace QuickLook.Plugin.DuckDbViewer;

/// <summary>
/// QuickLook entry point. This class is the only public type of the plugin and deliberately
/// contains no logic: detection lives in <see cref="FormatDetector"/>, everything else behind
/// <see cref="ViewerPanel"/>.
/// </summary>
/// <remarks>
/// QuickLook creates one long-lived instance to call <see cref="Init"/> and
/// <see cref="CanHandle"/>, and a fresh instance for every preview.
/// </remarks>
public sealed class Plugin : IViewer, IMoreMenu
{
    private ViewerPanel? _panel;

    // User plugins are consulted before built-in ones of the same priority, so the default
    // is enough to take precedence for the formats this plugin recognises.
    public int Priority => 0;

    /// <summary>
    /// Entries for QuickLook's "more" menu in the title bar. QuickLook reads this once, right
    /// after <see cref="View"/>; the commands enable themselves when there is data to export.
    /// </summary>
    public IEnumerable<IMenuItem> MenuItems
    {
        get
        {
            if (_panel is null)
                return [];

            return _panel.ViewModel.ExportActions.Select(action => new MoreMenuItem
            {
                Icon = FontSymbols.Export,
                Header = action.Title,
                Command = action.Command,
                IsVisible = true,
                IsEnabled = true,
            }).ToList();
        }
    }

    public void Init()
    {
        try
        {
            PluginEnvironment.EnsureInitialized();
        }
        catch (Exception e)
        {
            ProcessHelper.WriteLog($"DuckDbViewer failed to initialise: {e}");
        }
    }

    public bool CanHandle(string path)
    {
        return PluginEnvironment.IsReady
               && !Directory.Exists(path)
               && FormatDetector.Detect(path) != FileFormat.Unknown;
    }

    public void Prepare(string path, ContextObject context)
    {
        context.PreferredSize = new Size(1000, 680);
        context.TitlebarOverlap = false;
        context.TitlebarBlurVisibility = true;
        context.TitlebarColourVisibility = true;
    }

    public void View(string path, ContextObject context)
    {
        _panel = new ViewerPanel();
        context.ViewerContent = _panel;
        context.Title = Path.GetFileName(path);

        // Keep QuickLook's busy indicator up until the first rows (or an error) are on screen.
        _ = _panel.Open(path, () => context.IsBusy = false);
    }

    public void Cleanup()
    {
        _panel?.Dispose();
        _panel = null;
    }
}
