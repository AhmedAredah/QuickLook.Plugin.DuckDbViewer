using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace DuckDbViewer.Localization;

/// <summary>
/// User-visible text, looked up in <c>Translations.config</c> next to the assembly. The file
/// holds one element per language; the UI language is tried first, then its parent languages
/// (<c>pt-BR</c> falls back to <c>pt</c>), then English.
/// </summary>
internal static class Strings
{
    private const string FallbackLanguage = "en";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Table = new(
        () => Load(Path.Combine(AppEnvironment.BaseDirectory, "Translations.config"), CultureInfo.CurrentUICulture));

    public static string Get(string key) => Table.Value.TryGetValue(key, out var text) ? text : key;

    public static string Format(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, Get(key), args);
    }

    /// <summary>Builds the lookup table for one culture; a missing file yields an empty table.</summary>
    internal static IReadOnlyDictionary<string, string> Load(string file, CultureInfo culture)
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(file))
            return table;

        var root = XDocument.Load(file).Root;
        if (root is null)
            return table;

        // Least specific first, so that more specific languages overwrite their fallbacks.
        var languages = new List<string>();
        for (var current = culture; !string.IsNullOrEmpty(current.Name); current = current.Parent)
            languages.Insert(0, current.Name);
        languages.Insert(0, FallbackLanguage);

        foreach (var language in languages)
        {
            if (root.Element(language) is not { } section)
                continue;

            foreach (var entry in section.Elements())
            {
                if (!string.IsNullOrEmpty(entry.Value))
                    table[entry.Name.LocalName] = entry.Value;
            }
        }

        return table;
    }
}
