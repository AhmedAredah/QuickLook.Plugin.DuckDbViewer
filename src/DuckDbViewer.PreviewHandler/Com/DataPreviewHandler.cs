using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DuckDbViewer.PreviewHandler.Com;

/// <summary>
/// One preview, driven by the host through the shell's preview handler protocol:
/// <c>Initialize</c> (which file), <c>SetWindow</c>/<c>SetRect</c> (where to draw),
/// <c>DoPreview</c> (show it) and <c>Unload</c> (the user moved on).
/// </summary>
/// <remarks>
/// The preview is a WPF tree living in a child window of the host's window, which belongs to
/// another process. COM delivers calls to a .NET object on arbitrary threads, so every member
/// that touches state or UI hops to the server's UI thread first.
/// </remarks>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class DataPreviewHandler : IPreviewHandler, IPreviewHandlerVisuals, IInitializeWithFile,
    IInitializeWithItem, IObjectWithSite, IOleWindow
{
    private const uint SigdnFileSystemPath = 0x80058000;
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsClipChildren = 0x02000000;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    private readonly ComServer _server;
    private readonly Dispatcher _ui;
    private string? _path;
    private IntPtr _parent;
    private RECT _bounds;
    private Color? _hostBackground;
    private object? _site;
    private HwndSource? _window;
    private PreviewContent? _content;

    internal DataPreviewHandler(ComServer server, Dispatcher ui)
    {
        _server = server;
        _ui = ui;
    }

    /// <summary>Whether the host window this preview was given still exists.</summary>
    internal bool IsAttached => _parent == IntPtr.Zero || IsWindow(_parent);

    /// <summary>Whether a preview is currently on screen.</summary>
    internal bool IsShowing => _window is not null && IsWindow(_parent);

    // ---- Which file --------------------------------------------------------------------------

    public void Initialize(string filePath, uint mode) => _ui.Invoke(() => _path = filePath);

    public void Initialize(IShellItem item, uint mode)
    {
        item.GetDisplayName(SigdnFileSystemPath, out var path);
        _ui.Invoke(() => _path = path);
    }

    // ---- Where to draw -----------------------------------------------------------------------

    public void SetWindow(IntPtr hwnd, ref RECT rect)
    {
        var bounds = rect;
        _ui.Invoke(() =>
        {
            _parent = hwnd;
            _bounds = bounds;
            _server.Touch();

            if (_window is null)
                return;

            SetParent(_window.Handle, hwnd);
            ApplyBounds();
        });
    }

    public void SetRect(ref RECT rect)
    {
        var bounds = rect;
        _ui.Invoke(() =>
        {
            _bounds = bounds;
            ApplyBounds();
        });
    }

    public void SetBackgroundColor(uint color)
    {
        // COLORREF is 0x00BBGGRR.
        var background = Color.FromRgb((byte)(color & 0xFF), (byte)((color >> 8) & 0xFF), (byte)((color >> 16) & 0xFF));
        _ui.Invoke(() =>
        {
            _hostBackground = background;
            _content?.ApplyTheme(background);
        });
    }

    public void SetFont(IntPtr logFont)
    {
    }

    public void SetTextColor(uint color)
    {
    }

    // ---- Showing and hiding ------------------------------------------------------------------

    public void DoPreview() => _ui.Invoke(ShowPreview);

    private void ShowPreview()
    {
        _server.Touch();
        CloseWindow();

        if (_path is null || _parent == IntPtr.Zero)
            return;

        try
        {
            _content = new PreviewContent(_path);
            _content.ApplyTheme(_hostBackground);

            _window = new HwndSource(new HwndSourceParameters("DuckDbViewer preview")
            {
                ParentWindow = _parent,
                WindowStyle = WsChild | WsVisible | WsClipChildren,
                PositionX = _bounds.Left,
                PositionY = _bounds.Top,
                Width = Math.Max(_bounds.Width, 1),
                Height = Math.Max(_bounds.Height, 1),
            })
            {
                RootVisual = _content.Root,
            };
        }
        catch (Exception e)
        {
            Log.Write($"DoPreview failed for '{_path}'", e);
            CloseWindow();
            throw;
        }
    }

    public void Unload()
    {
        _ui.Invoke(() =>
        {
            _server.Touch();
            CloseWindow();
            _path = null;
        });
    }

    private void CloseWindow()
    {
        _content?.Dispose();
        _content = null;
        _window?.Dispose();
        _window = null;
    }

    private void ApplyBounds()
    {
        if (_window is null)
            return;

        SetWindowPos(_window.Handle, IntPtr.Zero, _bounds.Left, _bounds.Top,
            Math.Max(_bounds.Width, 1), Math.Max(_bounds.Height, 1), SwpNoZOrder | SwpNoActivate);
    }

    // ---- Focus and keyboard ------------------------------------------------------------------

    public void SetFocus()
    {
        _ui.Invoke(() =>
        {
            if (_window is not null)
                SetFocus(_window.Handle);
        });
    }

    public void QueryFocus(out IntPtr hwnd) => hwnd = _ui.Invoke(GetFocus);

    /// <summary>The preview is operated with the mouse; keys stay with the host.</summary>
    public int TranslateAccelerator(ref MSG message) => HResult.False;

    // ---- Plumbing ----------------------------------------------------------------------------

    public void SetSite(object? site) => _site = site;

    public int GetSite(ref Guid interfaceId, out IntPtr site)
    {
        site = IntPtr.Zero;
        if (_site is null)
            return HResult.Fail;

        var unknown = Marshal.GetIUnknownForObject(_site);
        try
        {
            return Marshal.QueryInterface(unknown, in interfaceId, out site);
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    public void GetWindow(out IntPtr hwnd) => hwnd = _ui.Invoke(() => _window?.Handle ?? _parent);

    public void ContextSensitiveHelp(bool enterMode)
    {
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", EntryPoint = "SetFocus")]
    private static extern IntPtr SetFocus(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();
}
