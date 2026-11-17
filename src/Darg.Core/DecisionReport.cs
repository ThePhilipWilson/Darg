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
        var why = Section(cardText, "Why it needs attention");
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
        if (why.Length > 0) sb.Append("## Why it needs attention\n\n").Append(why).Append("\n\n");
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

    /// <summary>Where a card's QA reports go once it is done (WOR-0102): beside the reports, not in the list.</summary>
    public const string ArchiveFolder = "archive";

    /// <summary>A card has reached the done column (WOR-0102): each of its QA reports (pending or not) goes to
    /// <c>archive/</c> beside the reports, marked approved if nobody settled it, so history survives and the list
    /// shows only what is open. Returns the reports as they were archived (their text carried into the done report).</summary>
    public static List<AgentReport> Archive(string folder, string card)
    {
        var moved = new List<AgentReport>();
        foreach (var r in ReportStore.List(folder).Where(r => r.Card == card && r.Qa))
        {
            try
            {
                if (r.Decision.Length == 0 || r.Decision == Pending) Settle(r.File, Approved, "");
                var archive = Path.Combine(folder, ArchiveFolder);
                Directory.CreateDirectory(archive);
                var to = Path.Combine(archive, Path.GetFileName(r.File));
                if (System.IO.File.Exists(to)) to = Path.Combine(archive, DateTime.Now.ToString("HHmmssfff") + "-" + Path.GetFileName(r.File));
                System.IO.File.Move(r.File, to);
                moved.Add(r with { File = to });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // left in the list; the next look tries again
        }
        return moved;
    }

    /// <summary>The done report for a card that has reached the done column (WOR-0102), in the Asked / Done / Left shape
    /// of an agent's own, from the QA reports just archived (their Changed and Checked carried over) or else the card
    /// and the commits naming it. Returns its file, or null when the card already has one: a card agent writes its own as
    /// its last step, so a finished report dated since the newest archived QA report (else within <paramref name="within"/>) stands.</summary>
    public static string? WriteDone(string folder, Card card, string cardFile, string repo, DateTime now, IReadOnlyList<AgentReport> archived, TimeSpan within)
    {
        var since = archived.Select(r => DateTime.TryParse(r.Date, out var d) ? d : (DateTime?)null).Where(d => d is not null).DefaultIfEmpty(null).Max();
        bool Counts(AgentReport r) => r.Card == card.Id && r.Kind == ReportKind.Finished
            && DateTime.TryParse(r.Date, out var d) && (since is { } s ? d >= s - TimeSpan.FromMinutes(1) : now - d <= within && now >= d - TimeSpan.FromMinutes(1));
        if (ReportStore.List(folder).Any(Counts)) return null;
        string text;
        try { text = System.IO.File.ReadAllText(cardFile); } catch (IOException) { text = ""; }
        var qa = archived.OrderByDescending(r => r.Date, StringComparer.Ordinal).FirstOrDefault();
        var commits = qa?.Commits is { Count: > 0 } c ? c : CommitsFor(repo, card.Id);
        var file = Path.Combine(folder, $"{now:yyyyMMdd-HHmm}-{card.Id}.md");
        if (System.IO.File.Exists(file)) return null;
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(file, BuildDone(card, text, commits, repo, qa, now), new UTF8Encoding(false));
        return file;
    }

    /// <summary>Clean up (WOR-0112): for every done card that has a QA report, in the list or already archived, archive
    /// what is still in the list and make sure a done report exists (written from the QA report when none does).
    /// Returns how many QA reports were archived and how many done reports written.</summary>
    public static (int Archived, int Written) CleanUp(string folder, IEnumerable<(Card Card, string File)> doneCards, Func<Card, string> repoFor, DateTime now)
    {
        int archived = 0, written = 0;
        var inArchive = Directory.Exists(Path.Combine(folder, ArchiveFolder)) ? ReportStore.List(Path.Combine(folder, ArchiveFolder)).Where(r => r.Qa).ToList() : [];
        foreach (var (card, file) in doneCards)
        {
            var moved = Archive(folder, card.Id);
            var before = inArchive.Where(r => r.Card == card.Id).ToList();
            if (moved.Count == 0 && before.Count == 0) continue; // never had a QA report: nothing to tidy
            archived += moved.Count;
            if (WriteDone(folder, card, file, repoFor(card), now, [.. moved, .. before], TimeSpan.MaxValue) is not null) written++;
        }
        return (archived, written);
    }

    public static string BuildDone(Card card, string cardText, IReadOnlyList<string> commits, string repo, AgentReport? qa, DateTime now)
    {
        static string Or(string s, string none) => s.Trim().Length > 0 ? s.Trim() : none;
        var body = qa?.Body ?? "";
        var asked = Or(Section(cardText, "Description"), card.Description);
        var changed = Section(body, "Changed");
        var checkedText = Section(body, "Checked");
        var notes = Section(cardText, "QA notes");
        var sb = new StringBuilder();
        sb.Append("---\n")
          .Append($"card: {card.Id}\n")
          .Append($"title: {card.Title}\n")
          .Append($"agent: {qa?.Agent}\n")
          .Append($"commits: {string.Join(' ', commits)}\n")
          .Append($"repo: {(repo.Length > 0 ? repo : qa?.Repo)}\n")
          .Append($"date: {now:yyyy-MM-dd HH:mm}\n")
          .Append("read: false\n---\n\n");
        sb.Append("## Asked\n\n").Append(Or(asked, "The card has no description.")).Append("\n\n");
        sb.Append("## Done\n\n").Append(Or(changed, commits.Count > 0 ? $"Commits naming {card.Id}: {string.Join(", ", commits)}." : "Nothing recorded: no QA report and no commit names this card."));
        if (checkedText.Length > 0) sb.Append("\n\nChecked: ").Append(checkedText);
        sb.Append("\n\nMoved to Done, so its QA report is in the reports archive folder.\n\n");
        sb.Append("## Left\n\n").Append(notes.Length > 0 ? "None. QA notes along the way:\n\n" + notes : "None.").Append('\n');
        return sb.ToString();
    }

    /// <summary>Settle a report's decision: the property, the ticked box and the notes in the Decision section.</summary>
    public static void Settle(string file, string decision, string notes)
    {
        // a card moved to done has its QA report archived before Approve gets to settle it: follow it there
        if (!System.IO.File.Exists(file) && Path.GetDirectoryName(file) is { } dir && System.IO.File.Exists(Path.Combine(dir, ArchiveFolder, Path.GetFileName(file))))
            file = Path.Combine(dir, ArchiveFolder, Path.GetFileName(file));
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
