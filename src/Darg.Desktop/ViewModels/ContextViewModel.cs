using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>The Context section (WOR-0145): the instructions (CLAUDE.md files), memory and Darg's own additions a new agent in
/// this project starts with, each readable here. Read-only; Open sends a file to the app markdown files open in.</summary>
public partial class ContextViewModel(ProjectConfig project) : ObservableObject
{
    public ObservableCollection<ContextItem> Items { get; } = [];
    [ObservableProperty] public partial ContextItem? Selected { get; set; }
    [ObservableProperty] public partial string Text { get; set; } = "";
    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty] public partial bool CanOpen { get; set; }

    public void Reload()
    {
        var keep = Selected;
        var items = ContextFiles.List(project);
        Items.Clear();
        foreach (var i in items) Items.Add(i);
        Selected = Items.FirstOrDefault(i => keep is not null && i.Path == keep.Path && i.Name == keep.Name) ?? Items.FirstOrDefault();
        var n = Items.Count(i => i.Group == ContextFiles.InstructionsGroup);
        var m = Items.Count(i => i.Group == ContextFiles.MemoryGroup);
        Status = $"{n} instruction file{(n == 1 ? "" : "s")}, {m} memory file{(m == 1 ? "" : "s")} for {project.WorkingFolder()}";
    }

    partial void OnSelectedChanged(ContextItem? value)
    {
        Text = value is null ? "" : ContextFiles.Read(value);
        CanOpen = value is { Path.Length: > 0 };
    }

    [RelayCommand]
    void Open()
    {
        if (Selected is not { Path.Length: > 0 } s) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(s.Path) { UseShellExecute = true }); }
        catch (Exception e) { Status = $"Can't open {s.Path}: {e.Message}"; }
    }
}
