using System.Runtime.CompilerServices;
using Avalonia.Headless.XUnit;
using Darg.Desktop.ViewModels;
using Microsoft.Diagnostics.Runtime;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0226: a window closed (a test's, or a closed project's views) is let go. Every test's window used to
/// stay alive for the rest of the run, held by static events and a project's timer, and each theme switch restyled
/// all of them: the theme tests took 4 s run early and 85 s run late, and the run held ~17 MB a test.</summary>
public class LeakTests
{
    static readonly Section[] Sections = [Section.Board, Section.Notes, Section.Docs, Section.Design, Section.Agents, Section.Flows,
        Section.Reports, Section.Mistakes, Section.Context, Section.Code, Section.GitHub, Section.Home];

    [AvaloniaFact]
    public async Task A_closed_window_and_its_view_models_are_let_go()
    {
        var (window, main, project) = await OpenAndClose();
        for (var i = 0; i < 3 && (window.IsAlive || main.IsAlive || project.IsAlive); i++)
        {
            await DargApp.Settle(200);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        if (window.IsAlive || main.IsAlive || project.IsAlive)
            Assert.Fail($"Still alive after the window closed: window {window.IsAlive}, main view model {main.IsAlive}, project {project.IsAlive}. What holds them:"
                + Environment.NewLine + LeakRoots.Paths("Darg.Desktop.MainWindow", "Darg.Desktop.ViewModels.MainViewModel", "Darg.Desktop.ViewModels.ProjectViewModel"));
    }

    // kept out of the test's own frame, so nothing on its stack holds what it made
    [MethodImpl(MethodImplOptions.NoInlining)]
    static async Task<(WeakReference, WeakReference, WeakReference)> OpenAndClose()
    {
        using var app = DargApp.Make(nameof(A_closed_window_and_its_view_models_are_let_go));
        app.Card("a card in progress", "In Progress", "Some words about it.");
        await app.Start();
        foreach (var s in Sections) await app.Api("show", new { project = "proj", section = s.ToString() });
        return (new WeakReference(app.Window), new WeakReference(app.Main), new WeakReference(app.Project));
    }
}

/// <summary>What keeps objects of some types alive: a shortest path from a GC root to each, read from a snapshot of
/// this process with ClrMD. For a leak test's failure message, so it says where to look without a rerun.</summary>
public static class LeakRoots
{
    public static string Paths(params string[] types)
    {
        var wanted = types.ToHashSet();
        var lines = new List<string>();
        using var target = DataTarget.CreateSnapshotAndAttach(Environment.ProcessId);
        var heap = target.ClrVersions[0].CreateRuntime().Heap;
        var parent = new Dictionary<ulong, (ulong From, string How)>();
        var queue = new Queue<ulong>();
        foreach (var root in heap.EnumerateRoots())
        {
            var o = root.Object.Address;
            if (o == 0 || !parent.TryAdd(o, (0, $"root: {root.RootKind}"))) continue;
            queue.Enqueue(o);
        }
        var found = new List<ulong>();
        while (queue.Count > 0 && found.Count < 6)
        {
            var at = queue.Dequeue();
            var obj = heap.GetObject(at);
            if (obj.Type?.Name is { } name && wanted.Contains(name)) { found.Add(at); continue; }
            foreach (var r in obj.EnumerateReferencesWithFields(carefully: false, considerDependantHandles: true))
            {
                var to = r.Object.Address;
                if (to != 0 && parent.TryAdd(to, (at, r.Field?.Name ?? (r.IsDependentHandle ? "dependent handle" : "item")))) queue.Enqueue(to);
            }
        }
        foreach (var f in found)
        {
            var path = new List<string>();
            for (var a = f; a != 0; a = parent[a].From) path.Add($"  {heap.GetObject(a).Type?.Name} (via {parent[a].How})");
            path.Reverse();
            lines.Add($"{heap.GetObject(f).Type?.Name}:");
            lines.AddRange(path);
        }
        return lines.Count == 0 ? "(no path from a root found)" : string.Join(Environment.NewLine, lines);
    }
}
