using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>The Context section (WOR-0145): the instructions (CLAUDE.md files), memory and Darg's own additions a new agent in
/// this project starts with, each readable here; Open sends a file to the app markdown files open in. And the MCP servers its
/// agents start (WOR-0208), a tick each, saved in instance.json as the project's <c>mcp</c>.</summary>
public partial class ContextViewModel(ProjectConfig project) : ObservableObject
{
    public ObservableCollection<ContextItem> Items { get; } = [];
    [ObservableProperty] public partial ContextItem? Selected { get; set; }
    [ObservableProperty] public partial string Text { get; set; } = "";
    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty] public partial bool CanOpen { get; set; }
    /// <summary>The MCP servers its agents could start, ticked or not.</summary>
    public ObservableCollection<McpTick> Servers { get; } = [];
    [ObservableProperty] public partial string ServersNote { get; set; } = "";
    /// <summary>Where the picks are saved: the instance folder (null: the usual one).</summary>
    public string? InstanceFolder { get; init; }

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
        ReloadServers();
    }

    void ReloadServers()
    {
        List<McpServers.Row> rows;
        try { rows = McpServers.Rows(project); }
        catch (Exception e) { rows = []; ServersNote = $"Couldn't read the MCP servers: {e.Message}"; }
        Servers.Clear();
        foreach (var r in rows) Servers.Add(new McpTick(this, r));
        Count();
    }

    void Count()
    {
        var on = Servers.Count(s => s.On);
        ServersNote = Servers.Count == 0 ? "None set up here. Agents start with no MCP servers."
            : on == 0 ? "None ticked: agents start with no MCP servers."
            : $"{on} of {Servers.Count} ticked: new agents start with {(on == 1 ? "that one" : "those")} only.";
    }

    /// <summary>A tick changed: the project's picks, and instance.json, take it at once, so the next agent started (or resumed) has it.</summary>
    internal void Ticked(McpTick tick)
    {
        McpServers.Tick(project, tick.Server, tick.On);
        try
        {
            var config = InstanceConfig.Load(InstanceFolder);
            if (config.Projects.FirstOrDefault(p => string.Equals(p.Id, project.Id, StringComparison.OrdinalIgnoreCase)) is { } saved)
            {
                saved.Mcp = project.Mcp;
                config.Save(InstanceFolder);
            }
            Count();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ServersNote = $"Couldn't save the pick: {e.Message}";
        }
        // a kept server unticked has nothing left to show it by
        if (tick.Kept && !tick.On) Servers.Remove(tick);
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

/// <summary>One MCP server on the Context page, with its tick.</summary>
public partial class McpTick : ObservableObject
{
    readonly ContextViewModel _owner;
    readonly bool _loading;
    public McpServer Server { get; }
    public bool Kept { get; }
    public string Name => Server.Name;
    /// <summary>Where it's from, and why it's ticked by default if it is: "~/.claude.json", "plugin claude-draw · on by default: Darg's ✎ Draw uses it".</summary>
    public string Source => Server.Source + (Server.DefaultOn && Server.Why is { } why ? $" · on by default: {why}" : "");
    public string Tip => Server.Runs.Length > 0 ? $"{Server.Name}: {Server.Runs}" : Server.Name;
    [ObservableProperty] public partial bool On { get; set; }

    public McpTick(ContextViewModel owner, McpServers.Row row)
    {
        _owner = owner;
        Server = row.Server;
        Kept = row.Kept;
        _loading = true;
        On = row.On;
        _loading = false;
    }

    partial void OnOnChanged(bool value)
    {
        if (!_loading) _owner.Ticked(this);
    }
}
