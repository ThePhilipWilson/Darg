using System.Diagnostics;

namespace Darg.Core;

/// <summary>How many copies of Darg agents may run at once on this machine (WOR-0170). Agents launch debug builds to
/// check their work, each about 650 MB with its web view, and one night copies from several QA and card agents ran
/// beside two emulators and five test suites until Windows ran out of memory and the PC bluescreened.
///
/// The cap counts debug and QA copies only. The installed copy (with <see cref="Updater.SourceFile"/> beside its exe)
/// and the live copy (<see cref="LiveCopy"/>, from the live worktree) are Philip's own: never counted, never refused.
/// DARG_MAX_COPIES sets the cap; 0 or less turns it off.</summary>
public static class Copies
{
    public const int DefaultMax = 4;
    public const string MaxVar = "DARG_MAX_COPIES";

    public static int Max(string? setting) => int.TryParse(setting, out var n) ? n : DefaultMax;

    /// <summary>Whether the copy at this exe is Philip's own: the installed one, or the live one.</summary>
    public static bool Exempt(string exe)
    {
        var dir = Path.GetDirectoryName(exe) ?? "";
        if (File.Exists(Path.Combine(dir, Updater.SourceFile))) return true;
        var p = exe.Replace('\\', '/');
        return p.Contains("/.claude/worktrees/live/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Why this copy shouldn't start, with the others' exes running now, or null if it may.</summary>
    public static string? Refusal(string self, IEnumerable<string> others, int max, Func<string, bool> exempt)
    {
        if (max <= 0 || exempt(self)) return null;
        var counted = others.Where(o => !exempt(o)).ToList();
        if (counted.Count < max) return null;
        var where = string.Join("; ", counted.GroupBy(o => o, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Count() > 1 ? $"{g.Count()} from {g.Key}" : g.Key));
        return $"Darg didn't start: {counted.Count} other debug or QA copies are running already, and at most {max} may run at once " +
               $"({MaxVar}). Close one you have finished with first: {where}";
    }

    /// <summary>The exes of the other copies of Darg running now (this process left out).</summary>
    public static IReadOnlyList<string> OthersRunning()
    {
        var me = Environment.ProcessId;
        var list = new List<string>();
        foreach (var p in Process.GetProcessesByName("Darg.Desktop"))
            using (p)
            {
                if (p.Id == me) continue;
                try { list.Add(p.MainModule?.FileName ?? "unknown"); }
                catch (Exception) { list.Add("unknown"); } // ended meanwhile, or not ours to look at: still a copy
            }
        return list;
    }
}
