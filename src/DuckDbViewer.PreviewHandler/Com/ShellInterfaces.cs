using System;
using System.Runtime.InteropServices;

namespace DuckDbViewer.PreviewHandler.Com;

// The COM contracts of a Windows shell preview handler. Method order is the vtable order
// and must match the Windows SDK headers (shobjidl_core.h, propsys.h, ocidl.h, oleidl.h).

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly int Width => Right - Left;

    public readonly int Height => Bottom - Top;
}

[StructLayout(LayoutKind.Sequential)]
public struct MSG
{
    public IntPtr Hwnd;
    public uint Message;
    public IntPtr WParam;
    public IntPtr LParam;
    public uint Time;
    public int PointX;
    public int PointY;
}

internal static class HResult
{
    public const int Ok = 0;
    public const int False = 1;
    public const int NoInterface = unchecked((int)0x80004002);
    public const int NoAggregation = unchecked((int)0x80040110);
    public const int Fail = unchecked((int)0x80004005);
}

[ComImport]
[ComVisible(true)]
[Guid("8895b1c6-b41f-4c1c-a562-0d564250836f")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPreviewHandler
{
    void SetWindow(IntPtr hwnd, ref RECT rect);

    void SetRect(ref RECT rect);

    void DoPreview();

    void Unload();

    void SetFocus();

    void QueryFocus(out IntPtr hwnd);

    [PreserveSig]
    int TranslateAccelerator(ref MSG message);
}

/// <summary>Lets the host tell the handler which colours to blend in with.</summary>
[ComImport]
[ComVisible(true)]
[Guid("196bf9a5-b346-4ef0-aa1e-5dcdb76768b1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPreviewHandlerVisuals
{
    void SetBackgroundColor(uint color);

    void SetFont(IntPtr logFont);

    void SetTextColor(uint color);
}

[ComImport]
[ComVisible(true)]
[Guid("b7d14566-0509-4cce-a71f-0a554233bd9b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IInitializeWithFile
{
    void Initialize([MarshalAs(UnmanagedType.LPWStr)] string filePath, uint mode);
}

[ComImport]
[ComVisible(true)]
[Guid("7f73be3f-fb79-493c-a6c7-7ee14e245841")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IInitializeWithItem
{
    void Initialize(IShellItem item, uint mode);
}

[ComImport]
[ComVisible(true)]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItem
{
    void BindToHandler(IntPtr bindContext, ref Guid handlerId, ref Guid interfaceId, out IntPtr result);

    void GetParent(out IShellItem parent);

    void GetDisplayName(uint nameKind, [MarshalAs(UnmanagedType.LPWStr)] out string name);

    void GetAttributes(uint mask, out uint attributes);

    void Compare(IShellItem other, uint hint, out int order);
}

[ComImport]
[ComVisible(true)]
[Guid("fc4801a3-2ba9-11cf-a229-00aa003d7352")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IObjectWithSite
{
    void SetSite([MarshalAs(UnmanagedType.IUnknown)] object? site);

    [PreserveSig]
    int GetSite(ref Guid interfaceId, out IntPtr site);
}

[ComImport]
[ComVisible(true)]
[Guid("00000114-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IOleWindow
{
    void GetWindow(out IntPtr hwnd);

    void ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enterMode);
}

[ComImport]
[ComVisible(true)]
[Guid("00000001-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(IntPtr outer, ref Guid interfaceId, out IntPtr instance);

    [PreserveSig]
    int LockServer([MarshalAs(UnmanagedType.Bool)] bool shouldLock);
}
