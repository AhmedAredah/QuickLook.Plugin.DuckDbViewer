using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QuickLook.Plugin.DuckDbViewer.Native;
using QuickLook.Plugin.DuckDbViewer.Views;

namespace DuckDbViewer.DevHost;

/// <summary>
/// Hosts the preview panel in a plain window.
/// <code>
/// DuckDbViewer.DevHost &lt;file&gt; [--theme light|dark] [--select &lt;object&gt;] [--tab schema]
///                      [--page &lt;number&gt;] [--size &lt;width&gt;x&lt;height&gt;] [--screenshot &lt;png&gt;]
/// </code>
/// With <c>--screenshot</c> the window is rendered off-screen to an image and the host exits,
/// which makes UI changes reviewable without touching the desktop.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            MessageBox.Show(
                "Usage: DuckDbViewer.DevHost <file> [--theme light|dark] [--select <object>] " +
                "[--tab schema] [--page <number>] [--size <width>x<height>] [--screenshot <png>]",
                "DuckDbViewer.DevHost");
            return 2;
        }

        var path = Path.GetFullPath(args[0]);
        var dark = !string.Equals(Option(args, "--theme"), "light", StringComparison.OrdinalIgnoreCase);
        var size = (Option(args, "--size") ?? "1000x680").Split('x').Select(double.Parse).ToArray();
        var screenshot = Option(args, "--screenshot");

        PluginEnvironment.EnsureInitialized();

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Resources.MergedDictionaries.Add(LoadTheme(dark));

        var panel = new ViewerPanel();
        var window = new Window
        {
            Title = Path.GetFileName(path),
            Width = size[0],
            Height = size[1],
            Content = panel,
            UseLayoutRounding = true,
        };
        window.SetResourceReference(Window.BackgroundProperty, "MainWindowBackgroundNoTransparent");
        window.Closed += (_, _) => panel.Dispose();

        if (screenshot is not null)
        {
            window.WindowStyle = WindowStyle.None;
            window.ShowInTaskbar = false;
            window.ShowActivated = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000;
            window.Top = -20000;
        }

        window.Loaded += async (_, _) =>
        {
            try
            {
                await Drive(panel, path, args);
                if (screenshot is null)
                    return;

                await Settle(window.Dispatcher);
                Capture((FrameworkElement)window.Content, Path.GetFullPath(screenshot));
                window.Close();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                Environment.ExitCode = 1;
                window.Close();
            }
        };

        app.Run(window);
        return Environment.ExitCode;
    }

    /// <summary>Opens the file and applies the requested selection, tab and page.</summary>
    private static async Task Drive(ViewerPanel panel, string path, string[] args)
    {
        var viewModel = panel.ViewModel;
        await panel.Open(path, () => { });

        if (Option(args, "--select") is { } name)
        {
            viewModel.SelectedObject = viewModel.Objects.FirstOrDefault(o => o.DisplayName == name)
                ?? throw new ArgumentException($"No object named '{name}'.");
            await WaitUntil(() => !viewModel.IsLoading && (viewModel.Page.TotalRows.HasValue || viewModel.HasNotice));
        }

        if (Option(args, "--page") is { } page)
            await viewModel.GoToPageAsync(int.Parse(page) - 1);

        if (string.Equals(Option(args, "--tab"), "schema", StringComparison.OrdinalIgnoreCase))
            viewModel.IsSchemaVisible = true;
    }

    private static ResourceDictionary LoadTheme(bool dark)
    {
        var name = dark ? "MainWindowStyles.Dark.xaml" : "MainWindowStyles.xaml";
        return new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/QuickLook.Common;component/Styles/{name}"),
        };
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The panel did not finish loading.");
            await Task.Delay(50);
        }
    }

    /// <summary>Lets data binding, layout and rendering catch up.</summary>
    private static async Task Settle(Dispatcher dispatcher)
    {
        for (var i = 0; i < 3; i++)
        {
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(100);
        }
    }

    private static void Capture(FrameworkElement element, string file)
    {
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);

        // The panel is transparent; paint the window background behind it.
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
            context.DrawRectangle((Brush)element.FindResource("MainWindowBackgroundNoTransparent"), null, bounds);
            context.DrawRectangle(new VisualBrush(element), null, bounds);
        }

        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var stream = File.Create(file);
        encoder.Save(stream);
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
