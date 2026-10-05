using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DuckDbViewer.Detection;
using Microsoft.Win32;

namespace DuckDbViewer.PreviewHandler;

/// <summary>
/// Registers the handler with Windows for the current user. Everything lives under
/// <c>HKEY_CURRENT_USER</c>, so no administrator rights are needed and uninstalling leaves
/// nothing behind.
/// </summary>
internal static class Registration
{
    public static readonly Guid HandlerClsid = new("70896f61-f867-48d0-a2a6-4c156d20e55e");

    private const string DisplayName = "DuckDbViewer Preview Handler";

    /// <summary>The shell's identifier for "preview handler" under a file extension's <c>shellex</c> key.</summary>
    private const string PreviewHandlerCategory = "{8895b1c6-b41f-4c1c-a562-0d564250836f}";

    private const string ClassesRoot = @"Software\Classes";
    private const string PreviewHandlersKey = @"Software\Microsoft\Windows\CurrentVersion\PreviewHandlers";

    /// <summary>Remembers per-user handlers that were replaced, so unregistering can restore them.</summary>
    private const string ReplacedHandlersKey = @"Software\DuckDbViewer\ReplacedPreviewHandlers";

    /// <summary>
    /// Every extension the viewer can open. PowerToys Peek prefers its own previewer for
    /// <c>.db</c>, <c>.sqlite</c> and <c>.sqlite3</c>; registering them still enables the
    /// preview pane of File Explorer.
    /// </summary>
    private static IReadOnlyCollection<string> Extensions => FormatDetector.SupportedExtensions;

    private static string ClsidText => HandlerClsid.ToString("B");

    public static void Register(string executablePath)
    {
        using (var clsid = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\CLSID\{ClsidText}"))
        {
            clsid.SetValue(null, DisplayName);
            using var server = clsid.CreateSubKey("LocalServer32");
            server.SetValue(null, $"\"{executablePath}\"");
        }

        using (var handlers = Registry.CurrentUser.CreateSubKey(PreviewHandlersKey))
            handlers.SetValue(ClsidText, DisplayName);

        using var replaced = Registry.CurrentUser.CreateSubKey(ReplacedHandlersKey);
        foreach (var extension in Extensions)
        {
            using var key = Registry.CurrentUser.CreateSubKey(HandlerKeyPath(extension));
            if (key.GetValue(null) is string previous && previous != ClsidText)
                replaced.SetValue(extension, previous);

            key.SetValue(null, ClsidText);
        }

        NotifyShell();
    }

    public static void Unregister()
    {
        using (var replaced = Registry.CurrentUser.OpenSubKey(ReplacedHandlersKey))
        {
            foreach (var extension in Extensions)
                RemoveAssociation(extension, replaced?.GetValue(extension) as string);
        }

        using (var handlers = Registry.CurrentUser.OpenSubKey(PreviewHandlersKey, writable: true))
            handlers?.DeleteValue(ClsidText, throwOnMissingValue: false);

        Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesRoot}\CLSID\{ClsidText}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(ReplacedHandlersKey, throwOnMissingSubKey: false);
        DeleteIfEmpty(@"Software\DuckDbViewer");
        NotifyShell();
    }

    private static string HandlerKeyPath(string extension)
    {
        return $@"{ClassesRoot}\{extension}\shellex\{PreviewHandlerCategory}";
    }

    /// <summary>
    /// Undoes the association of one extension, but only if it still points at this handler,
    /// and removes the keys that registering created without touching anything else in them.
    /// </summary>
    private static void RemoveAssociation(string extension, string? previousHandler)
    {
        var path = HandlerKeyPath(extension);
        using (var key = Registry.CurrentUser.OpenSubKey(path, writable: true))
        {
            if (key?.GetValue(null) as string != ClsidText)
                return;

            if (previousHandler is not null)
            {
                key.SetValue(null, previousHandler);
                return;
            }
        }

        Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        DeleteIfEmpty($@"{ClassesRoot}\{extension}\shellex");
        DeleteIfEmpty($@"{ClassesRoot}\{extension}");
    }

    private static void DeleteIfEmpty(string path)
    {
        using (var key = Registry.CurrentUser.OpenSubKey(path))
        {
            if (key is null || key.SubKeyCount > 0 || key.ValueCount > 0)
                return;
        }

        Registry.CurrentUser.DeleteSubKey(path, throwOnMissingSubKey: false);
    }

    private static void NotifyShell()
    {
        const int AssociationChanged = 0x08000000;
        SHChangeNotify(AssociationChanged, 0, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
