using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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

    // SPIKE: a made-up extension, so that no real file association is touched yet.
    private static readonly IReadOnlyCollection<string> Extensions = [".duckpeek"];

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

        foreach (var extension in Extensions)
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{extension}\shellex\{PreviewHandlerCategory}");
            key.SetValue(null, ClsidText);
        }

        NotifyShell();
    }

    public static void Unregister()
    {
        foreach (var extension in Extensions)
        {
            // Only remove the association if it is still ours.
            var path = $@"{ClassesRoot}\{extension}\shellex\{PreviewHandlerCategory}";
            using (var key = Registry.CurrentUser.OpenSubKey(path))
            {
                if (key?.GetValue(null) as string != ClsidText)
                    continue;
            }

            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }

        using (var handlers = Registry.CurrentUser.OpenSubKey(PreviewHandlersKey, writable: true))
            handlers?.DeleteValue(ClsidText, throwOnMissingValue: false);

        Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesRoot}\CLSID\{ClsidText}", throwOnMissingSubKey: false);
        NotifyShell();
    }

    private static void NotifyShell()
    {
        const int AssociationChanged = 0x08000000;
        SHChangeNotify(AssociationChanged, 0, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
