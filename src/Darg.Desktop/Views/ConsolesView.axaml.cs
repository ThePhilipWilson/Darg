using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Iciclecreek.Terminal;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Views;

/// <summary>One TerminalControl per console tab, all kept in the tree (so each keeps running) with only
/// the selected one visible. Closing a tab ends its process.</summary>
public partial class ConsolesView : UserControl
{
    readonly Dictionary<ConsoleTab, TerminalControl> _terms = [];
    ConsolesViewModel? _vm;

    public ConsolesView()
    {
        InitializeComponent();
        TabDrag.Attach(ConsoleTabs, (from, to) => (DataContext as ConsolesViewModel)?.Tabs.Move(from, to));
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) { _vm.Tabs.CollectionChanged -= TabsChanged; _vm.PropertyChanged -= Changed; _vm.Closing -= Kill; }
        _vm = DataContext as ConsolesViewModel;
        if (_vm is null) return;
        _vm.Tabs.CollectionChanged += TabsChanged;
        _vm.PropertyChanged += Changed;
        _vm.Closing += Kill;
        foreach (var t in _vm.Tabs) Add(t);
        Show();
    }

    void TabsChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        foreach (ConsoleTab t in e.NewItems ?? Array.Empty<ConsoleTab>()) Add(t);
        foreach (ConsoleTab t in e.OldItems ?? Array.Empty<ConsoleTab>())
            if (_terms.Remove(t, out var term)) Hosts.Children.Remove(term);
        Show();
    }

    void Changed(object? s, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(ConsolesViewModel.Selected)) Show(); }

    void Add(ConsoleTab t)
    {
        if (_terms.ContainsKey(t)) return;
        var term = new TerminalControl
        {
            Process = t.Process, StartingDirectory = t.Cwd, FontSize = 13, BufferSize = 10000,
            Background = Brushes.Black, Margin = new Thickness(6), IsVisible = false,
            ProcessArgs = t.Args.ToList(), // its default list is a fixed-size array: replace it, don't add to it
        };
        term.ProcessExited += (_, _) => t.Ended = true;
        t.Reader = n => ReadLines(term, n);
        _terms[t] = term;
        Hosts.Children.Add(term);
    }

    /// <summary>The last n lines of the console's text, scrollback included, trailing blank lines dropped.</summary>
    static string ReadLines(TerminalControl term, int n)
    {
        var lines = Avalonia.Threading.Dispatcher.UIThread.CheckAccess() ? Collect(term) : Avalonia.Threading.Dispatcher.UIThread.Invoke(() => Collect(term));
        var end = lines.Count;
        while (end > 0 && lines[end - 1].Length == 0) end--;
        if (end > 1 && ConsolesViewModel.IsBarePrompt(lines[end - 1])) end--;
        return string.Join('\n', lines.Skip(Math.Max(0, end - n)).Take(n));
    }

    static dynamic? Buffer(TerminalControl term)
    {
        try { return term.Terminal?.Buffer; } catch (NullReferenceException) { return null; }
    }

    static List<string> Collect(TerminalControl term)
    {
        var lines = new List<string>();
        // the control's terminal exists only once it has been drawn and started: before that there's nothing to read
        var buf = Buffer(term);
        if (buf is null) return lines;
        for (var i = 0; i < buf.Lines.Length; i++)
            lines.Add(buf.Lines[i]?.TranslateToString(true) ?? "");
        return lines;
    }

    void Show()
    {
        foreach (var (tab, term) in _terms) term.IsVisible = tab == _vm?.Selected;
        if (_vm?.Selected is { } sel && _terms.TryGetValue(sel, out var t)) Avalonia.Threading.Dispatcher.UIThread.Post(() => t.Focus());
    }

    void Kill(ConsoleTab t)
    {
        if (_terms.TryGetValue(t, out var term)) try { term.Kill(); } catch (Exception) { }
    }
}
