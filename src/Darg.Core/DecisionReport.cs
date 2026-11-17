using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>The board's column for cards that need the person to decide (WOR-0092): one named for attention, or
/// "needs phil/me/you". Not the last column (that is done).</summary>
public static class AttentionColumn
{
    public static string? Find(IReadOnlyList<Column> columns) =>
        columns.Take(Math.Max(0, columns.Count - 1))
            .FirstOrDefault(c => Regex.IsMatch(c.Name, @"attention|\bneeds (phil|me|you)\b", RegexOptions.IgnoreCase))?.Name;
}

/// <summary>A decision report (WOR-0092): written by Darg itself when a card lands in the attention column, whoever
/// moved it, from what the card file holds (description, How to check, QA notes), the commits that name the card and
/// the newest report an agent wrote for it. It is a QA report (<c>status: qa</c>) ending in a Decision section, with
/// <c>decision: pending</c> in its properties until Approve or Send back in Reports settles it.</summary>
public static partial class DecisionReport
{
    public const string Pending = "pending", Approved = "approved", SentBack = "sent back";

    public static string FileName(string card, DateTime now) => $"{now:yyyyMMdd-HHmm}-{card}-qa.md";

    /// <summary>A QA report for the card dated within <paramref name="within"/> of now, if any: a card an agent just
    /// handed over with its own QA report needs no second one.</summary>
    public static AgentReport? Recent(IEnumerable<AgentReport> reports, string card, TimeSpan within, DateTime now) =>
        reports.Where(r => r.Card == card && r.Qa && DateTime.TryParse(r.Date, out var d) && now - d <= within && now >= d - TimeSpan.FromMinutes(1))
            .OrderByDescending(r => r.Date, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>The body of a <c>## Heading</c> section of a card file or report, or "" (up to the next ## heading).</summary>
    public static string Section(string text, string heading)
    {
        text = text.Replace("\r\n", "\n");
        var m = Regex.Match(text, @"(?:^|\n)##[ \t]+" + Regex.Escape(heading) + @"[ \t]*\n(.*?)(?=\n##[ \t]|\z)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }

    /// <summary>Short ids of the commits whose message names the card, newest first (best effort: none without git).</summary>
    public static IReadOnlyList<string> CommitsFor(string repo, string cardId)
    {
        if (repo.Length == 0 || !Repo.IsGit(repo)) return [];
        var psi = new ProcessStartInfo("git") { WorkingDirectory = repo, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "log", "--all", "-n", "8", "--format=%h", "--grep=" + cardId }) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEndAsync();
            p.StandardError.ReadToEnd();
            if (!p.WaitForExit(15_000)) { p.Kill(); return []; }
            return output.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
        catch (System.ComponentModel.Win32Exception) { return []; }
    }

    /// <param name="cardText">The card file as it is now.</param>
    /// <param name="agentReport">The newest report an agent wrote for this card, if any: its Checked section is the best account of what was verified.</param>
    public static string Build(Card card, string cardText, IReadOnlyList<string> commits, string repo, AgentReport? agentReport, DateTime now)
    {
        static string Or(string s, string none) => s.Trim().Length > 0 ? s.Trim() : none;
        var description = Or(Section(cardText, "Description"), card.Description);
        var how = Section(cardText, "How to check");
        var qaNotes = Section(cardText, "QA notes");
        var agentBody = agentReport?.Body ?? "";
        var changed = Section(agentBody, "Changed") is { Length: > 0 } c ? c : Section(agentBody, "Done");
        var look = Section(agentBody, "Look at");
        var checkedText = Section(agentBody, "Checked");
        var ids = commits.Count > 0 ? commits : agentReport?.Commits ?? [];
        var sb = new StringBuilder();
        sb.Append("---\nstatus: qa\n")
          .Append($"decision: {Pending}\n")
          .Append($"card: {card.Id}\n")
          .Append($"title: {card.Title}\n")
          .Append($"agent: {agentReport?.Agent}\n")
          .Append($"commits: {string.Join(' ', ids)}\n")
          .Append($"repo: {(repo.Length > 0 ? repo : agentReport?.Repo)}\n")
          .Append($"date: {now:yyyy-MM-dd HH:mm}\n")
          .Append("read: false\n---\n\n");
        sb.Append("## Asked\n\n").Append(Or(description, "The card has no description.")).Append("\n\n");
        sb.Append("## Changed\n\n").Append(Or(changed, ids.Count > 0
            ? $"No agent report says. Commits naming {card.Id}: {string.Join(", ", ids)}."
            : "Nothing recorded: no agent report and no commit names this card.")).Append("\n\n");
        sb.Append("## Look at\n\n").Append(Or(look.Length > 0 ? look : how, "Nothing written down: the card has no How to check section.")).Append("\n\n");
        if (look.Length > 0 && how.Length > 0) sb.Append("### How to check (from the card)\n\n").Append(how).Append("\n\n");
        sb.Append("## Checked\n\n").Append(Or(checkedText, "No agent report says what was verified, so treat it as unchecked.")).Append("\n\n");
        if (qaNotes.Length > 0) sb.Append("## Earlier QA notes\n\n").Append(qaNotes).Append("\n\n");
        sb.Append("## Decision\n\n")
          .Append("**Recommendation:** ").Append(Recommendation(how, checkedText, agentReport is not null)).Append("\n\n")
          .Append("- [ ] Approve (Approve in Reports moves the card to Done)\n")
          .Append("- [ ] Send back (Send back in Reports puts your notes in the card and starts an agent on them)\n\n")
          .Append("Notes:\n");
        return sb.ToString();
    }

    static string Recommendation(string how, string checkedText, bool hasAgentReport) =>
        !hasAgentReport && how.Length == 0 ? "Look at the card and its commits before approving: nothing here says what was done or how to try it."
        : checkedText.Length == 0 ? "Try it before approving: nothing says what was verified."
        : "Approve if what the Look at section describes behaves as written; send it back with notes if not.";

    /// <summary>Write the report for a card that has just reached the attention column; returns its file, or null when
    /// a QA report for it is that fresh already (an agent's own, or the last call).</summary>
    public static string? Write(string folder, Card card, string cardFile, string repo, DateTime now, TimeSpan within)
    {
        var existing = ReportStore.List(folder).Where(r => r.Card == card.Id).ToList();
        if (Recent(existing, card.Id, within, now) is not null) return null;
        string text;
        try { text = System.IO.File.ReadAllText(cardFile); } catch (IOException) { text = ""; }
        var agentReport = existing.Where(r => r.Qa && r.Decision.Length == 0).OrderByDescending(r => r.Date, StringComparer.Ordinal).FirstOrDefault();
        var report = Build(card, text, CommitsFor(repo, card.Id), repo, agentReport, now);
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, FileName(card.Id, now));
        System.IO.File.WriteAllText(file, report, new UTF8Encoding(false));
        return file;
    }

    /// <summary>Settle a report's decision: the property, the ticked box and the notes in the Decision section.</summary>
    public static void Settle(string file, string decision, string notes)
    {
        var text = System.IO.File.ReadAllText(file).Replace("\r\n", "\n");
        text = DecisionLine().IsMatch(text) ? DecisionLine().Replace(text, $"decision: {decision}", 1)
            : Regex.Replace(text, "^(status:.*)$", "$1\ndecision: " + decision, RegexOptions.Multiline);
        var box = decision == Approved ? "- [ ] Approve" : "- [ ] Send back";
        var at = text.IndexOf(box, StringComparison.Ordinal);
        if (at >= 0) text = text[..(at + 3)] + "x" + text[(at + 4)..];
        var n = text.LastIndexOf("\nNotes:", StringComparison.Ordinal);
        if (n >= 0) text = text[..n] + $"\nNotes: {notes.Trim()}\n";
        System.IO.File.WriteAllText(file, text, new UTF8Encoding(false));
    }

    [GeneratedRegex(@"^decision\s*:.*$", RegexOptions.Multiline)]
    private static partial Regex DecisionLine();
}
