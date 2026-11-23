using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Darg.Core;
using Span = Darg.Core.Span;

namespace Darg.Mobile.Views;

/// <summary>The phone's markdown (AND-0008): Darg.Core.Markdown's blocks drawn at phone sizes, as the desktop's
/// MarkdownView draws them but for a finger. A tap on a link raises <see cref="LinkTapped"/> with its target
/// ("wiki:Name" for [[Name]], else the URL); the page decides where it goes. Pictures are named, not drawn: they
/// are files on the PC. Colours by the Core theme keys, read from the app's resources as the blocks are built.</summary>
public sealed class MarkdownView : StackPanel
{
    public static readonly StyledProperty<string?> MarkdownProperty = AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));
    public string? Markdown { get => GetValue(MarkdownProperty); set => SetValue(MarkdownProperty, value); }

    /// <summary>The base text size; headings and code scale from it. 16 reads at arm's length on a phone.</summary>
    public static readonly StyledProperty<double> BaseSizeProperty = AvaloniaProperty.Register<MarkdownView, double>(nameof(BaseSize), 16);
    public double BaseSize { get => GetValue(BaseSizeProperty); set => SetValue(BaseSizeProperty, value); }

    /// <summary>Text colour for the whole view (a note's dark ink on its paper); unset, the theme's Ink.</summary>
    public static readonly StyledProperty<IBrush?> InkProperty = AvaloniaProperty.Register<MarkdownView, IBrush?>(nameof(Ink));
    public IBrush? Ink { get => GetValue(InkProperty); set => SetValue(InkProperty, value); }

    public event Action<string>? LinkTapped;

    static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, Roboto Mono, monospace");

    public MarkdownView() => Spacing = 10;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty || change.Property == BaseSizeProperty || change.Property == InkProperty) Build();
    }

    IBrush Brush(string key) =>
        Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var v) == true && v is IBrush b ? b : Brushes.Gray;

    void Build()
    {
        Children.Clear();
        foreach (var block in Core.Markdown.Parse(Markdown ?? "")) Children.Add(Draw(block));
    }

    Control Draw(MdBlock block) => block switch
    {
        MdHeading h => Text(h.Spans, size: BaseSize + h.Level switch { 1 => 8, 2 => 5, 3 => 3, _ => 1 }, bold: true),
        MdParagraph p => Text(p.Spans),
        MdList l => List(l),
        MdCode c => new Border
        {
            Background = Brush("Sunk"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = new TextBlock { Text = c.Text.TrimEnd('\n'), FontFamily = Mono, FontSize = BaseSize - 2, Foreground = Ink ?? Brush("Ink") },
            },
        },
        MdQuote q => new Border
        {
            BorderBrush = Brush("Accent"), BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(12, 2),
            Child = Text(q.Spans, brush: Brush("MdDim")),
        },
        MdTable t => Table(t),
        MdImages i => Pictures(i),
        _ => new Border { Height = 1, Background = Brush("Line"), Margin = new Thickness(0, 4) },
    };

    /// <summary>A row of pictures as named chips: the files are on the PC, and a phone has no business loading them.</summary>
    Control Pictures(MdImages row)
    {
        var panel = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        foreach (var image in row.Images)
        {
            var name = image.Alt.Length > 0 ? image.Alt : image.Src.Split('?')[0].Split('/', '\\')[^1];
            panel.Children.Add(new Border
            {
                BorderBrush = Brush("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4),
                Child = new TextBlock { Text = "🖼 " + name, Foreground = Brush("MdDim"), FontSize = BaseSize - 2, TextWrapping = TextWrapping.Wrap },
            });
        }
        return panel;
    }

    TextBlock Text(IReadOnlyList<Span> spans, double size = 0, bool bold = false, IBrush? brush = null)
    {
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = size > 0 ? size : BaseSize,
            Foreground = brush ?? Ink ?? Brush("Ink"), LineHeight = double.NaN,
        };
        if (bold) tb.FontWeight = FontWeight.SemiBold;
        foreach (var s in spans)
        {
            var run = new Run(s.Text);
            if (s.Bold) run.FontWeight = FontWeight.Bold;
            if (s.Italic) run.FontStyle = FontStyle.Italic;
            if (s.Code) { run.FontFamily = Mono; run.FontSize = tb.FontSize - 1.5; run.Background = Brush("LabelBg"); }
            if (s.Link is { } target)
            {
                run.Foreground = Brush("Info");
                run.TextDecorations = TextDecorations.Underline;
                // the run in a small text block of its own that takes the tap, its target kept in Tag (tests read it there)
                var link = new TextBlock { Tag = target, FontSize = tb.FontSize };
                link.Inlines!.Add(run);
                link.Tapped += (_, e) => { e.Handled = true; LinkTapped?.Invoke(target); };
                tb.Inlines!.Add(new InlineUIContainer(link) { BaselineAlignment = BaselineAlignment.TextBottom });
                continue;
            }
            tb.Inlines!.Add(run);
        }
        return tb;
    }

    /// <summary>Every link drawn, by target, in reading order: what a tap on each would follow.</summary>
    public IEnumerable<TextBlock> Links() =>
        Descendants(this).OfType<TextBlock>().Where(t => t.Tag is string);

    static IEnumerable<object> Descendants(object o)
    {
        switch (o)
        {
            case Panel p: foreach (var c in p.Children) { yield return c; foreach (var d in Descendants(c)) yield return d; } break;
            case Decorator d when d.Child is { } child: yield return child; foreach (var x in Descendants(child)) yield return x; break;
            case ContentControl cc when cc.Content is { } content: yield return content; foreach (var x in Descendants(content)) yield return x; break;
            case TextBlock tb when tb.Inlines is { } inlines:
                foreach (var ui in inlines.OfType<InlineUIContainer>())
                    if (ui.Child is { } c) { yield return c; foreach (var x in Descendants(c)) yield return x; }
                break;
        }
    }

    Control List(MdList l)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 6 };
        for (var i = 0; i < l.Items.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var mark = new TextBlock { Text = l.Ordered ? $"{l.Start + i}." : "•", Margin = new Thickness(2, 0, 10, 0), Foreground = Brush("MdDim"), FontSize = BaseSize };
            var text = Text(l.Items[i]);
            Grid.SetRow(mark, i); Grid.SetRow(text, i); Grid.SetColumn(text, 1);
            grid.Children.Add(mark); grid.Children.Add(text);
        }
        return grid;
    }

    /// <summary>A table scrolls sideways rather than squeezing its columns to a word each.</summary>
    Control Table(MdTable t)
    {
        var cols = t.Rows.Max(r => r.Count);
        var grid = new Grid();
        for (var c = 0; c < cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var r = 0; r < t.Rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var c = 0; c < t.Rows[r].Count; c++)
            {
                var text = Text(t.Rows[r][c], size: BaseSize - 1, bold: r == 0);
                text.MaxWidth = 260;
                var cell = new Border
                {
                    BorderBrush = Brush("Line"), BorderThickness = new Thickness(0, 0, 1, 1), Padding = new Thickness(8, 6),
                    Background = r == 0 ? Brush("Panel2") : null, Child = text,
                };
                Grid.SetRow(cell, r); Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new Border
            {
                BorderBrush = Brush("Line"), BorderThickness = new Thickness(1, 1, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Child = grid,
            },
        };
    }
}
