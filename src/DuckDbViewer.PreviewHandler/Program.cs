using System;
using System.Linq;
using System.Windows;
using DuckDbViewer.PreviewHandler.Com;

namespace DuckDbViewer.PreviewHandler;

internal static class Program
{
    /// <summary>
    /// <list type="bullet">
    /// <item><c>--register</c> / <c>--unregister</c>: add or remove the handler for the current user.</item>
    /// <item><c>-Embedding</c>: passed by COM when a host asks for a preview; runs the server.</item>
    /// </list>
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (Has(args, "register"))
            {
                Registration.Register(Environment.ProcessPath!);
                return 0;
            }

            if (Has(args, "unregister"))
            {
                Registration.Unregister();
                return 0;
            }

            if (Has(args, "embedding"))
                return ComServer.Run();

            MessageBox.Show(
                "This program shows previews of data files inside File Explorer and PowerToys Peek.\n\n" +
                "Run it with --register to enable the previews for your user account, " +
                "or with --unregister to remove them.",
                "DuckDbViewer Preview Handler");
            return 0;
        }
        catch (Exception e)
        {
            Log.Write("Fatal", e);
            return 1;
        }
    }

    /// <summary>Accepts <c>-name</c>, <c>--name</c> and <c>/name</c>, as COM and people write them.</summary>
    private static bool Has(string[] args, string name)
    {
        return args.Any(a => a.TrimStart('-', '/').Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
