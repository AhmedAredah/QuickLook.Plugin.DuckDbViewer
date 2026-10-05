using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace DuckDbViewer.PreviewHandler.Com;

/// <summary>
/// Runs this process as the COM local server of the preview handler: registers the class
/// factory, pumps messages on the single UI thread on which every COM call arrives, and
/// exits once it has been idle for a while.
/// </summary>
/// <remarks>
/// Hosts keep the class factory locked for as long as they run (PowerToys Peek stays in the
/// background permanently), so lock counts cannot decide when to exit. The server instead
/// exits when no preview is on screen any more; hosts transparently start it again.
/// </remarks>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class ComServer : IClassFactory
{
    private const uint ClsctxLocalServer = 0x4;
    private const uint RegclsMultipleUse = 0x1;

    private static readonly TimeSpan IdleCheckInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);

    private readonly Dispatcher _ui;
    private readonly List<DataPreviewHandler> _handlers = [];
    private DateTime _lastActivity = DateTime.UtcNow;

    private ComServer(Dispatcher ui) => _ui = ui;

    /// <summary>Serves previews until idle. Must be called on an STA thread.</summary>
    public static int Run()
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.DispatcherUnhandledException += (_, e) =>
        {
            // One failed preview must not take the server, and with it other previews, down.
            Log.Write("Unhandled exception", e.Exception);
            e.Handled = true;
        };

        var server = new ComServer(application.Dispatcher);
        var clsid = Registration.HandlerClsid;
        var hr = CoRegisterClassObject(ref clsid, server, ClsctxLocalServer, RegclsMultipleUse, out var cookie);
        if (hr < 0)
        {
            Log.Write($"CoRegisterClassObject failed: 0x{hr:X8}");
            return hr;
        }

        Log.Write("Server started");
        var idleTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = IdleCheckInterval };
        idleTimer.Tick += (_, _) =>
        {
            if (server.IsIdle())
                application.Shutdown();
        };
        idleTimer.Start();

        try
        {
            return application.Run();
        }
        finally
        {
            CoRevokeClassObject(cookie);
            Log.Write("Server stopped");
        }
    }

    public int CreateInstance(IntPtr outer, ref Guid interfaceId, out IntPtr instance)
    {
        instance = IntPtr.Zero;
        if (outer != IntPtr.Zero)
            return HResult.NoAggregation;

        try
        {
            // COM calls arrive on arbitrary threads; all server state belongs to the UI thread.
            var handler = _ui.Invoke(() =>
            {
                var created = new DataPreviewHandler(this, _ui);
                _handlers.Add(created);
                Touch();
                return created;
            });

            var unknown = Marshal.GetIUnknownForObject(handler);
            try
            {
                return Marshal.QueryInterface(unknown, in interfaceId, out instance);
            }
            finally
            {
                Marshal.Release(unknown);
            }
        }
        catch (Exception e)
        {
            Log.Write("CreateInstance", e);
            return HResult.Fail;
        }
    }

    public int LockServer(bool shouldLock) => HResult.Ok;

    /// <summary>Records that a host is using the server. UI thread only.</summary>
    internal void Touch() => _lastActivity = DateTime.UtcNow;

    private bool IsIdle()
    {
        _handlers.RemoveAll(h => !h.IsAttached);
        if (_handlers.Any(h => h.IsShowing))
        {
            Touch();
            return false;
        }

        return DateTime.UtcNow - _lastActivity > IdleTimeout;
    }

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(ref Guid clsid, [MarshalAs(UnmanagedType.IUnknown)] object factory,
        uint context, uint flags, out uint cookie);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint cookie);
}
