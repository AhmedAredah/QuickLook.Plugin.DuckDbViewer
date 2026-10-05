using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace QuickLook.Plugin.DuckDbViewer.Native;

/// <summary>
/// Makes the plugin's private dependencies loadable inside the QuickLook process.
/// </summary>
/// <remarks>
/// QuickLook loads plugins with <see cref="Assembly.LoadFrom(string)"/> and a plugin cannot add
/// binding redirects to the host's configuration, so two things need help:
/// <list type="bullet">
/// <item>managed dependencies whose shipped version differs from the referenced one;</item>
/// <item>the native DuckDB engine, which is not on the host's DLL search path.</item>
/// </list>
/// Nothing in here may reference a type of DuckDbViewer.Core or DuckDB.NET, otherwise the
/// runtime would try to resolve those assemblies before the resolver is installed.
/// </remarks>
internal static class PluginEnvironment
{
    private const string NativeLibraryName = "duckdb.dll";

    private static readonly object Gate = new();
    private static bool _attempted;

    /// <summary>The folder the plugin assembly was loaded from.</summary>
    public static string BaseDirectory { get; } =
        Path.GetDirectoryName(typeof(PluginEnvironment).Assembly.Location)!;

    /// <summary>Whether the engine can be used. When it cannot, the plugin declines every file.</summary>
    public static bool IsReady { get; private set; }

    /// <summary>Runs the one-time setup. Throws if the engine cannot be loaded.</summary>
    public static void EnsureInitialized()
    {
        lock (Gate)
        {
            if (_attempted)
                return;

            _attempted = true;
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            PreloadNativeLibrary();
            IsReady = true;
        }
    }

    private static Assembly? OnAssemblyResolve(object sender, ResolveEventArgs args)
    {
        // Only answer for our own dependency graph; other plugins resolve their own.
        var requester = args.RequestingAssembly;
        if (requester is null || requester.IsDynamic || !IsInBaseDirectory(requester.Location))
            return null;

        var candidate = Path.Combine(BaseDirectory, new AssemblyName(args.Name).Name + ".dll");
        return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
    }

    private static bool IsInBaseDirectory(string location)
    {
        return !string.IsNullOrEmpty(location) &&
               string.Equals(Path.GetDirectoryName(location), BaseDirectory, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Loads the engine by full path. Later P/Invokes into "duckdb" then bind to the module
    /// that is already in the process.
    /// </summary>
    private static void PreloadNativeLibrary()
    {
        var path = Path.Combine(BaseDirectory, NativeLibraryName);
        if (LoadLibraryW(path) == IntPtr.Zero)
            throw new DllNotFoundException($"Unable to load '{path}'.", new Win32Exception(Marshal.GetLastWin32Error()));
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryW(string lpLibFileName);
}
