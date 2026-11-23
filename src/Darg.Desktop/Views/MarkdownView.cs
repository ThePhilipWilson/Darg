using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Darg.Core;
using Span = Darg.Core.Span;

namespace Darg.Desktop.Views;

/// <summary>Draws markdown (Darg.Core.Markdown's blocks) as selectable text: headings, lists,
/// code blocks, quotes, tables and rules, with bold, italic, inline code and links. Code copies in one
/// click: a code block has a Copy button, and a plain click on inline code copies it, so a command an
/// agent says to type goes straight to the clipboard.</summary>
public sealed class MarkdownView : StackPanel
{
    public static readonly StyledProperty<string?> MarkdownProperty = AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));
    public string? Markdown { get => GetValue(MarkdownProperty); set => SetValue(MarkdownProperty, value); }
    /// <summary>The folder a picture's relative path is taken from (a README's repo, a doc's folder). Unset, only
    /// pictures with a full path show; the rest are links.</summary>
    public static readonly StyledProperty<string?> BaseFolderProperty = AvaloniaProperty.Register<MarkdownView, string?>(nameof(BaseFolder));
    public string? BaseFolder { get => GetValue(BaseFolderProperty); set => SetValue(BaseFolderProperty, value); }

    static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");
    // the theme's colours, read as the blocks are built; a theme switch rebuilds them (Retheme)
    static IBrush CodeBg => ThemeManager.Brush("Sunk");
    static IBrush InlineCodeBg => ThemeManager.Brush("LabelBg");
    static IBrush Line => ThemeManager.Brush("Line");
    static IBrush Dim => ThemeManager.Brush("MdDim");
    static IBrush Link => ThemeManager.Brush("Info");
    static IBrush Accent => ThemeManager.Brush("Accent");
    static IBrush HeaderRow => ThemeManager.Brush("Panel2");

    /// <summary>A link was clicked: its target ("wiki:Name" for an Obsidian [[link]], else the URL). Card links
    /// (card:WOR-0158) go to <see cref="OpenCard"/> instead, in every view.</summary>
    public event Action<string>? LinkClicked;

    /// <summary>Set by the app (WOR-0158): open a card's panel by its ID, false when no project has it. Unset,
    /// card links draw as plain links that do nothing.</summary>
    public static Func<string, bool>? OpenCard { get; set; }
    /// <summary>Set by the app (WOR-0192): open a link to a markdown file (relative to the work in hand, or absolute)
    /// in the Docs viewer. A view with no LinkClicked handler (transcripts, Fettle) tries it before handing a link
    /// to the OS. Unset, such links go to the OS as before.</summary>
    public static Func<string, bool>? OpenDoc { get; set; }

    /// <summary>A link to a markdown file: a path ending in .md or .markdown (fragment and query left off); web, mail,
    /// card, wiki and other-scheme links aren't. A drive path (D:/x.md) and a file: link are.</summary>
    public static bool IsDocLink(string link)
    {
        link = link.Trim().Trim('<', '>');
        if (link.StartsWith("wiki:", StringComparison.Ordinal) || CardLinks.IdOf(link) is not null) return false;
        var colon = link.IndexOf(':');
        if (colon > 1 && !link.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return false; // another scheme
        var path = link.Split('#', '?')[0];
        return path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);
    }
    /// <summary>Set by the app: whether a prefix (WOR) is one of the instance's boards', so a bare WOR-0158 in the
    /// text is linked to its card. Unset, only card: links are.</summary>
    public static Func<string, bool>? IsCardPrefix { get; set; }

    public MarkdownView() => Spacing = 8;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty || change.Property == BaseFolderProperty) Build();
    }

    Theme? _builtIn;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ThemeManager.Changed += Retheme;
        Retheme();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ThemeManager.Changed -= Retheme;
    }

    void Retheme()
    {
        if (_builtIn is not null && _builtIn != ThemeManager.Current) Build();
    }

    void Build()
    {
        _builtIn = ThemeManager.Current;
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
        MdImages i => Pictures(i),
        _ => new Border { Height = 1, Background = Line, Margin = new Thickness(0, 4) },
    };

    static readonly HashSet<string> Drawable = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };

    /// <summary>A row of pictures: each one on this machine drawn (no wider than the page, never blown up), a click
    /// opening its link or the picture itself; one on the web or in a format Avalonia can't draw (SVG badges) is a
    /// link named by its alt text.</summary>
    Control Pictures(MdImages row)
    {
        var panel = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, HorizontalAlignment = row.Centred ? HorizontalAlignment.Center : HorizontalAlignment.Left };
        foreach (var image in row.Images)
        {
            var file = LocalFile(image.Src);
            var bitmap = file is not null && Drawable.Contains(Path.GetExtension(file)) ? Load(file) : null;
            var target = image.Link ?? file ?? image.Src;
            Control shown;
            if (bitmap is not null)
            {
                // one picture alone may fill the page; several in a row stay small enough to sit together
                shown = new Image
                {
                    Source = bitmap, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
                    MaxHeight = row.Images.Count == 1 ? 560 : 220,
                    // the width an HTML img asks for, as GitHub shows it
                    MaxWidth = image.Width ?? (row.Images.Count == 1 ? double.PositiveInfinity : 420),
                };
            }
            else
            {
                var name = image.Alt.Length > 0 ? image.Alt : Path.GetFileName(image.Src.Split('?')[0]);
                shown = new Border
                {
                    BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2),
                    Child = new TextBlock { Text = "🖼 " + name, Foreground = Link, FontSize = 12 },
                };
            }
            ToolTip.SetTip(shown, image.Alt.Length > 0 && image.Alt != target ? $"{image.Alt}\n{target}" : target);
            shown.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
            shown.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return;
                e.Handled = true;
                if (image.Link is not null) Open(image.Link);
                else Shell(file ?? image.Src); // the picture itself, full size, in the person's viewer
            };
            panel.Children.Add(shown);
        }
        return panel;
    }

    /// <summary>A picture's file on this machine: a full path, or one relative to <see cref="BaseFolder"/>
    /// ("/docs/x.png" too, as GitHub reads it from the repo's top). Null for a URL or a file that isn't there.</summary>
    string? LocalFile(string src)
    {
        if (src.Contains("://", StringComparison.Ordinal) || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var path = Uri.UnescapeDataString(src.Split('?', '#')[0]);
            var full = Path.IsPathRooted(path) && File.Exists(path) ? path
                : BaseFolder is { } b ? Path.GetFullPath(Path.Combine(b, path.TrimStart('/', '\\'))) : null;
            return full is not null && File.Exists(full) ? full : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    static Avalonia.Media.Imaging.Bitmap? Load(string file)
    {
        try { using var f = File.OpenRead(file); return Avalonia.Media.Imaging.Bitmap.DecodeToWidth(f, 1400); }
        catch (Exception) { return null; }
    }

    static void Shell(string target)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception) { }
    }

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
        // a fixed line height clips the descenders of a line holding a link (an embedded control), so those size themselves
        if (IsCardPrefix is { } isPrefix) spans = CardLinks.Link(spans, isPrefix);
        var tb = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = size > 0 || spans.Any(s => s.Link is not null) ? double.NaN : 21 };
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
                if (LinkClicked is not null || IsWeb(s.Link) || (OpenDoc is not null && IsDocLink(s.Link)) || (OpenCard is not null && CardLinks.IdOf(s.Link) is not null))
                {
                    // a clickable link: the run inside a small text block that takes the click
                    var target = s.Link;
                    var link = new TextBlock { Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
                    ToolTip.SetTip(link, CardLinks.IdOf(target) is { } cardId ? $"Open {cardId}" : target.StartsWith("wiki:", StringComparison.Ordinal) ? target[5..] : target);
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

    /// <summary>A card link opens the card, wherever it is (WOR-0158). Otherwise a view that handles links (Docs)
    /// gets every click; anywhere else (agent transcripts, Tools answers) a web or mail link opens in the browser.</summary>
    void Open(string target)
    {
        if (CardLinks.IdOf(target) is { } card) { OpenCard?.Invoke(card); return; }
        if (LinkClicked is not null) { LinkClicked(target); return; }
        if (OpenDoc is not null && IsDocLink(target) && OpenDoc(target)) return;
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
                    Background = r == 0 ? HeaderRow : null,
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
