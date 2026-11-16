using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Iciclecreek.Terminal;
using Workbench.Desktop.ViewModels;

namespace Workbench.Desktop.Views;

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
        _terms[t] = term;
        Hosts.Children.Add(term);
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
