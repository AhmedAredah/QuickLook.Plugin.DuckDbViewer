using System.Globalization;
using System.IO;
using DuckDbViewer.Localization;
using Xunit;

namespace DuckDbViewer.Tests;

public sealed class StringsTests
{
    private const string Sample =
        "<Translations>" +
        "<en><Greeting>Hello</Greeting><Farewell>Goodbye</Farewell><Thanks>Thanks</Thanks></en>" +
        "<pt><Greeting>Olá</Greeting><Farewell>Adeus</Farewell></pt>" +
        "<pt-BR><Greeting>Oi</Greeting><Thanks></Thanks></pt-BR>" +
        "</Translations>";

    private static string WriteSample()
    {
        var file = Path.Combine(Path.GetTempPath(), "DuckDbViewerTests-" + Path.GetRandomFileName() + ".config");
        File.WriteAllText(file, Sample);
        return file;
    }

    [Fact]
    public void A_language_falls_back_to_its_parent_and_then_to_english()
    {
        var file = WriteSample();
        try
        {
            var table = Strings.Load(file, CultureInfo.GetCultureInfo("pt-BR"));

            Assert.Equal("Oi", table["Greeting"]);       // the language itself
            Assert.Equal("Adeus", table["Farewell"]);    // its parent
            Assert.Equal("Thanks", table["Thanks"]);     // an empty entry does not hide the fallback
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void An_unknown_language_gets_english()
    {
        var file = WriteSample();
        try
        {
            Assert.Equal("Hello", Strings.Load(file, CultureInfo.GetCultureInfo("ja-JP"))["Greeting"]);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void A_missing_file_or_key_degrades_to_the_key()
    {
        Assert.Empty(Strings.Load(Path.Combine(Path.GetTempPath(), "does-not-exist.config"), CultureInfo.InvariantCulture));
        Assert.Equal("No_Such_Key", Strings.Get("No_Such_Key"));
    }

    [Fact]
    public void The_shipped_translations_are_found_and_complete()
    {
        var file = Path.Combine(AppEnvironment.BaseDirectory, "Translations.config");
        var english = Strings.Load(file, CultureInfo.GetCultureInfo("en"));
        var arabic = Strings.Load(file, CultureInfo.GetCultureInfo("ar"));

        Assert.Equal("Data", english["Tab_Data"]);
        Assert.NotEqual(english["Tab_Data"], arabic["Tab_Data"]);
        Assert.Equal(english.Count, arabic.Count);
    }
}
