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
        if (change.Property == MarkdownProperty || change.Property == BaseFolderProperty) Build();
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
