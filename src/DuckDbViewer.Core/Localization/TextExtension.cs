using System;
using System.Windows.Markup;

namespace DuckDbViewer.Localization;

/// <summary>XAML access to <see cref="Strings"/>: <c>Text="{l:Text Tab_Data}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
internal sealed class TextExtension : MarkupExtension
{
    public TextExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
}
