using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Darg.Mobile.Views;

/// <summary>A note's paper colour, the desktop wall's own (fixed whatever the theme, as post-its are).</summary>
public static class NotePaper
{
    static readonly Dictionary<string, IBrush> Paper = new(StringComparer.OrdinalIgnoreCase)
    {
        ["yellow"] = new SolidColorBrush(Color.Parse("#f2dd7a")), ["pink"] = new SolidColorBrush(Color.Parse("#f2a7c3")),
        ["blue"] = new SolidColorBrush(Color.Parse("#9fd0f2")), ["green"] = new SolidColorBrush(Color.Parse("#b6e3a0")),
        ["orange"] = new SolidColorBrush(Color.Parse("#f5bf86")),
    };

    public static readonly IValueConverter Brush =
        new FuncValueConverter<string?, IBrush>(c => Paper.GetValueOrDefault(c ?? "", Paper["yellow"]));
}
