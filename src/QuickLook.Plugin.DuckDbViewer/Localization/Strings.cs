using System.Globalization;
using System.IO;
using QuickLook.Common.Helpers;
using QuickLook.Plugin.DuckDbViewer.Native;

namespace QuickLook.Plugin.DuckDbViewer.Localization;

/// <summary>
/// User-visible text, looked up in <c>Translations.config</c> next to the plugin assembly.
/// QuickLook's helper picks the UI language and falls back to English.
/// </summary>
internal static class Strings
{
    private static readonly string TranslationFile =
        Path.Combine(PluginEnvironment.BaseDirectory, "Translations.config");

    public static string Get(string key) => TranslationHelper.Get(key, TranslationFile, failsafe: key);

    public static string Format(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, Get(key), args);
    }
}
