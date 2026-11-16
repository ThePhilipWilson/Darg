using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media;
using Iciclecreek.Terminal;
using Workbench.Desktop.ViewModels;

namespace Workbench.Desktop.Views;

public partial class ProjectView : UserControl
{
    ProjectViewModel? _vm;

    public ProjectView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) _vm.PropertyChanged -= Changed;
        _vm = DataContext as ProjectViewModel;
        if (_vm is not null) _vm.PropertyChanged += Changed;
        // The section may have been opened before this view was attached.
        Changed(_vm, new PropertyChangedEventArgs(nameof(ProjectViewModel.TerminalStarted)));
    }

    /// <summary>The terminal is made the first time its section opens, so a project's shell doesn't
    /// start until it's wanted; after that it stays, running, while other sections or tabs are shown.</summary>
    void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ProjectViewModel.TerminalStarted) || _vm is not { TerminalStarted: true } || TerminalHost.Child is not null) return;
        var term = new TerminalControl
        {
            Process = _vm.Shell,
            StartingDirectory = _vm.TerminalFolder,
            FontSize = 13,
            BufferSize = 5000,
            Background = Brushes.Black,
        };
        term.ProcessExited += (_, _) => TerminalHost.Child = null;
        TerminalHost.Child = term;
        term.Focus();
    }
}
