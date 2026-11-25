namespace Darg.Core;

/// <summary>Where a card stands, beyond its column (WOR-0214): what the board's split view sorts a column's cards into,
/// a narrow sub-column per state. In the order the sub-columns are drawn, left to right.</summary>
public enum CardState { Blocked, Ready, Working, RunningQa, NeedsYou, Failed }

/// <summary>A card's state, worked out each time from what is live (its agent, a QA run, its blockers, its column), never
/// stored; the split view's sub-columns, one per state a column has cards in; and the merged columns of the All board.</summary>
public static class CardStates
{
    /// <summary>A card's state. An agent asking (a question, a permission) comes first, then a QA run at work, then any
    /// other agent at work; with none at work, a card in the attention column waits on you, a failed agent says so, an
    /// open blocker holds it, and anything else is ready.</summary>
    public static CardState Of(bool agentWorking, bool agentNeedsYou, bool agentFailed, bool qaRunning, bool inAttention, bool blocked) =>
        agentNeedsYou ? CardState.NeedsYou
        : qaRunning ? CardState.RunningQa
        : agentWorking ? CardState.Working
        : inAttention ? CardState.NeedsYou
        : agentFailed ? CardState.Failed
        : blocked ? CardState.Blocked
        : CardState.Ready;

    /// <summary>A sub-column's heading. <paramref name="kind"/> is the list's kind (<see cref="BoardLayouts.KindOf"/>):
    /// ready in QA is ready to check.</summary>
    public static string Label(CardState state, string kind = "") => state switch
    {
        CardState.Blocked => "Blocked",
        CardState.Ready => kind == "qa" ? "QA ready" : "Ready to go",
        CardState.Working => "Agent working",
        CardState.RunningQa => "Running QA",
        CardState.NeedsYou => "Needs you",
        _ => "Agent failed",
    };

    /// <summary>Whether a list of this kind splits: the done column and Completed hold finished cards, so they don't.</summary>
    public static bool Splits(string kind) => kind is not ("done" or "completed");

    /// <summary>Items grouped by state, in <see cref="CardState"/> order, each group keeping the items' own order; a
    /// state no item is in has no group.</summary>
    public static IReadOnlyList<(CardState State, IReadOnlyList<T> Items)> Group<T>(IEnumerable<T> items, Func<T, CardState> stateOf)
    {
        var by = new Dictionary<CardState, List<T>>();
        foreach (var item in items)
        {
            var s = stateOf(item);
            if (!by.TryGetValue(s, out var list)) by[s] = list = [];
            list.Add(item);
        }
        return Enum.GetValues<CardState>().Where(by.ContainsKey).Select(s => (s, (IReadOnlyList<T>)by[s])).ToList();
    }

    /// <summary>The All board's lists (WOR-0214): every board's lists merged by name (case aside), in the order the boards
    /// have them. A name only some boards have goes in after the one before it on the first board that has it.</summary>
    public static List<string> MergeColumns(IEnumerable<IReadOnlyList<string>> boards)
    {
        var merged = new List<string>();
        int IndexOf(string name) => merged.FindIndex(m => m.Equals(name, StringComparison.OrdinalIgnoreCase));
        foreach (var names in boards)
        {
            var after = -1;
            foreach (var name in names)
            {
                var at = IndexOf(name);
                if (at < 0) { at = after + 1; merged.Insert(at, name); }
                after = at;
            }
        }
        return merged;
    }
}

/// <summary>How a project's Board section is viewed (WOR-0214), kept in the instance beside shown-boards (never in the
/// boards folder, which a third-party board app reads): whether the All board is picked, and which boards are split by
/// state, a board's name a line (<see cref="AllKey"/> for the All board).</summary>
public static class BoardViews
{
    /// <summary>The All board's name in the split list: no board folder can be called it.</summary>
    public const string AllKey = "*";

    public sealed class View(bool all, HashSet<string> split)
    {
        public bool All { get; set; } = all;
        public HashSet<string> Split { get; } = split;
    }

    public static string FileFor(string projectId, string? instanceFolder = null) =>
        Path.Combine(Path.GetDirectoryName(ShownBoards.FileFor(projectId, instanceFolder))!, "board-views");

    public static View Load(string projectId, string? instanceFolder = null)
    {
        var split = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = false;
        try
        {
            var file = FileFor(projectId, instanceFolder);
            if (File.Exists(file))
                foreach (var line in File.ReadAllLines(file))
                {
                    var p = line.Split('\t');
                    if (p[0].Trim() == "all") all = true;
                    else if (p[0].Trim() == "split" && p.Length > 1 && p[1].Trim().Length > 0) split.Add(p[1].Trim());
                }
        }
        catch (Exception) { }
        return new View(all, split);
    }

    public static void Save(string projectId, View view, string? instanceFolder = null)
    {
        try
        {
            var file = FileFor(projectId, instanceFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllLines(file, (view.All ? ["all"] : Array.Empty<string>())
                .Concat(view.Split.Order(StringComparer.OrdinalIgnoreCase).Select(b => "split\t" + b)));
        }
        catch (Exception) { }
    }
}
