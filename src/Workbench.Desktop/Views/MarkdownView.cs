using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Workbench.Core;
using Span = Workbench.Core.Span;

namespace Workbench.Desktop.Views;

/// <summary>Draws markdown (Workbench.Core.Markdown's blocks) as selectable text: headings, lists,
/// code blocks, quotes, tables and rules, with bold, italic, inline code and links.</summary>
public sealed class MarkdownView : StackPanel
{
    public static readonly StyledProperty<string?> MarkdownProperty = AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));
    public string? Markdown { get => GetValue(MarkdownProperty); set => SetValue(MarkdownProperty, value); }

    static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");
    static readonly IBrush CodeBg = new SolidColorBrush(Color.Parse("#101215"));
    static readonly IBrush InlineCodeBg = new SolidColorBrush(Color.Parse("#2a2f39"));
    static readonly IBrush Line = new SolidColorBrush(Color.Parse("#2e333d"));
    static readonly IBrush Dim = new SolidColorBrush(Color.Parse("#a9b0bc"));
    static readonly IBrush Link = new SolidColorBrush(Color.Parse("#6aa5e8"));
    static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#e0a84a"));

    public MarkdownView() => Spacing = 8;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty) Build();
    }

    void Build()
    {
        Children.Clear();
        foreach (var block in Core.Markdown.Parse(Markdown ?? "")) Children.Add(Draw(block));
    }

    Control Draw(MdBlock block) => block switch
    {
        MdHeading h => Text(h.Spans, size: h.Level switch { 1 => 20, 2 => 17, 3 => 15, _ => 14 }, bold: true),
        MdParagraph p => Text(p.Spans),
        MdList l => List(l),
        MdCode c => new Border
        {
            Background = CodeBg, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = new SelectableTextBlock { Text = c.Text, FontFamily = Mono, FontSize = 12 },
            },
        },
        MdQuote q => new Border
        {
            BorderBrush = Accent, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(10, 2),
            Child = Text(q.Spans, brush: Dim),
        },
        MdTable t => Table(t),
        _ => new Border { Height = 1, Background = Line, Margin = new Thickness(0, 4) },
    };

    static SelectableTextBlock Text(IReadOnlyList<Span> spans, double size = 0, bool bold = false, IBrush? brush = null)
    {
        var tb = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = size > 0 ? double.NaN : 21 };
        if (size > 0) tb.FontSize = size;
        if (bold) tb.FontWeight = FontWeight.SemiBold;
        if (brush is not null) tb.Foreground = brush;
        foreach (var s in spans)
        {
            var run = new Run(s.Text);
            if (s.Bold) run.FontWeight = FontWeight.Bold;
            if (s.Italic) run.FontStyle = FontStyle.Italic;
            if (s.Code) { run.FontFamily = Mono; run.FontSize = 12.5; run.Background = InlineCodeBg; }
            if (s.Link is not null) { run.Foreground = Link; run.TextDecorations = TextDecorations.Underline; }
            tb.Inlines!.Add(run);
        }
        return tb;
    }

    static Control List(MdList l)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 3 };
        for (var i = 0; i < l.Items.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var mark = new TextBlock { Text = l.Ordered ? $"{l.Start + i}." : "•", Margin = new Thickness(4, 0, 8, 0), Foreground = Dim };
            var text = Text(l.Items[i]);
            Grid.SetRow(mark, i); Grid.SetRow(text, i); Grid.SetColumn(text, 1);
            grid.Children.Add(mark); grid.Children.Add(text);
        }
        return grid;
    }

    static Control Table(MdTable t)
    {
        var cols = t.Rows.Max(r => r.Count);
        var grid = new Grid();
        for (var c = 0; c < cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var r = 0; r < t.Rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var c = 0; c < t.Rows[r].Count; c++)
            {
                var cell = new Border
                {
                    BorderBrush = Line, BorderThickness = new Thickness(0, 0, 1, 1), Padding = new Thickness(8, 4),
                    Background = r == 0 ? new SolidColorBrush(Color.Parse("#22262e")) : null,
                    Child = Text(t.Rows[r][c], bold: r == 0),
                };
                Grid.SetRow(cell, r); Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }
        return new Border
        {
            BorderBrush = Line, BorderThickness = new Thickness(1, 1, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = grid },
        };
    }
}
