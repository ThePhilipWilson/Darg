using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Workbench.Core;
using Span = Workbench.Core.Span;

namespace Workbench.Desktop.Views;

/// <summary>Draws markdown (Workbench.Core.Markdown's blocks) as selectable text: headings, lists,
/// code blocks, quotes, tables and rules, with bold, italic, inline code and links. Code copies in one
/// click: a code block has a Copy button, and a plain click on inline code copies it, so a command an
/// agent says to type goes straight to the clipboard.</summary>
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

    /// <summary>A link was clicked: its target ("wiki:Name" for an Obsidian [[link]], else the URL).</summary>
    public event Action<string>? LinkClicked;

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
        MdCode c => CodeBlock(c.Text),
        MdQuote q => new Border
        {
            BorderBrush = Accent, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(10, 2),
            Child = Text(q.Spans, brush: Dim),
        },
        MdTable t => Table(t),
        _ => new Border { Height = 1, Background = Line, Margin = new Thickness(0, 4) },
    };

    Control CodeBlock(string text)
    {
        var copy = new Button
        {
            Content = "Copy", FontSize = 11, Padding = new Thickness(8, 2), MinHeight = 0,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 5, 5, 0), Opacity = 0.75,
        };
        ToolTip.SetTip(copy, "Copy to clipboard");
        copy.Click += async (_, _) =>
        {
            if (!await Copy(text)) return;
            copy.Content = "Copied";
            DispatcherTimer.RunOnce(() => copy.Content = "Copy", TimeSpan.FromSeconds(1.5));
        };
        var block = new Border
        {
            // room on the right so the button never sits over the first line's text
            Background = CodeBg, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 70, 8),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = new SelectableTextBlock { Text = text, FontFamily = Mono, FontSize = 12 },
            },
        };
        return new Panel { Children = { block, copy } };
    }

    async Task<bool> Copy(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return false;
        await clipboard.SetTextAsync(text);
        return true;
    }

    SelectableTextBlock Text(IReadOnlyList<Span> spans, double size = 0, bool bold = false, IBrush? brush = null)
    {
        var tb = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = size > 0 ? double.NaN : 21 };
        var codes = new List<(int Start, int End, string Text)>();
        var at = 0;
        if (size > 0) tb.FontSize = size;
        if (bold) tb.FontWeight = FontWeight.SemiBold;
        if (brush is not null) tb.Foreground = brush;
        foreach (var s in spans)
        {
            var run = new Run(s.Text);
            if (s.Bold) run.FontWeight = FontWeight.Bold;
            if (s.Italic) run.FontStyle = FontStyle.Italic;
            if (s.Code) { run.FontFamily = Mono; run.FontSize = 12.5; run.Background = InlineCodeBg; }
            if (s.Link is not null)
            {
                run.Foreground = Link; run.TextDecorations = TextDecorations.Underline;
                if (LinkClicked is not null || IsWeb(s.Link))
                {
                    // a clickable link: the run inside a small text block that takes the click
                    var target = s.Link;
                    var link = new TextBlock { Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
                    ToolTip.SetTip(link, target.StartsWith("wiki:", StringComparison.Ordinal) ? target[5..] : target);
                    link.Inlines!.Add(run);
                    // The link takes the press itself. Left to bubble, SelectableTextBlock's OnPointerPressed captures
                    // the pointer, and the release then goes to the paragraph, not the link: clicks were lost.
                    link.PointerPressed += (_, e) =>
                    {
                        if (!e.GetCurrentPoint(link).Properties.IsLeftButtonPressed) return;
                        e.Pointer.Capture(link);
                        e.Handled = true;
                    };
                    link.PointerReleased += (_, e) =>
                    {
                        if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left || e.Pointer.Captured != link) return;
                        e.Pointer.Capture(null);
                        e.Handled = true;
                        if (new Rect(link.Bounds.Size).Contains(e.GetPosition(link))) Open(target); // let go off the link: no click
                    };
                    tb.Inlines!.Add(new InlineUIContainer(link));
                    at += 1; // an embedded control takes one character of the laid-out text
                    continue;
                }
            }
            if (s.Code) codes.Add((at, at + s.Text.Length, s.Text));
            at += s.Text.Length;
            tb.Inlines!.Add(run);
        }
        if (codes.Count > 0) CopyOnClick(tb, codes);
        return tb;
    }

    static bool IsWeb(string link) =>
        link.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || link.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || link.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    /// <summary>A view that handles links (Docs) gets every click; anywhere else (agent transcripts, Tools
    /// answers) a web or mail link opens in the browser.</summary>
    void Open(string target)
    {
        if (LinkClicked is not null) { LinkClicked(target); return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception) { }
    }

    /// <summary>Inline code under the pointer shows a hand and copies on a plain click (a drag still selects).
    /// Found by hit-testing the laid-out text, so the paragraph still wraps and selects as one piece.</summary>
    void CopyOnClick(SelectableTextBlock tb, List<(int Start, int End, string Text)> codes)
    {
        string? CodeAt(Avalonia.Input.PointerEventArgs e)
        {
            var hit = tb.TextLayout.HitTestPoint(e.GetPosition(tb) - new Point(tb.Padding.Left, tb.Padding.Top));
            if (!hit.IsInside) return null;
            foreach (var c in codes) if (hit.TextPosition >= c.Start && hit.TextPosition < c.End) return c.Text;
            return null;
        }
        var hand = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        tb.PointerMoved += (_, e) =>
        {
            var over = !e.GetCurrentPoint(tb).Properties.IsLeftButtonPressed && CodeAt(e) is not null;
            tb.Cursor = over ? hand : null;
            ToolTip.SetTip(tb, over ? "Click to copy" : null);
        };
        tb.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, async (_, e) =>
        {
            if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left || tb.SelectionStart != tb.SelectionEnd) return;
            if (CodeAt(e) is not { } text || !await Copy(text)) return;
            ToolTip.SetTip(tb, "Copied");
            ToolTip.SetIsOpen(tb, true);
            DispatcherTimer.RunOnce(() => ToolTip.SetIsOpen(tb, false), TimeSpan.FromSeconds(1.2));
        }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
    }

    Control List(MdList l)
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
