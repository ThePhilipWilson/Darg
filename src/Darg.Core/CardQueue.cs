using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>A named queue of a project's cards (WOR-0212): Darg works them one at a time, in order, starting an agent on
/// the next (as Work on does) once the one before has finished. A project has as many as it likes, each with its own name,
/// and a queue's cards may sit on any of the project's boards. Kept in <c>queues.json</c> in the project's working folder
/// (<see cref="QueueStore"/>), so a queue carries on after Darg restarts.</summary>
public sealed class CardQueue
{
    public string Name { get; set; } = "";
    /// <summary>The cards, in the order they run.</summary>
    public List<string> Cards { get; set; } = [];
    /// <summary>Nothing new starts while paused: a new queue until Run, a failure until Resume, or Pause pressed.</summary>
    public bool Paused { get; set; } = true;
    /// <summary>Why it's paused, in words for the strip; null for a queue that hasn't been run yet.</summary>
    public string? Why { get; set; }
    /// <summary>The card whose failure paused it: Resume moves on past it.</summary>
    public string? PausedOn { get; set; }
    /// <summary>The cards the queue has started (or found already at work), in the order it did.</summary>
    public List<string> Started { get; set; } = [];
    /// <summary>Started cards it has moved on from: done, waiting on the person, or in QA with nothing waiting on that QA.</summary>
    public List<string> Released { get; set; } = [];
    /// <summary>For a card that reached QA: whether its QA check blocks the next card, as the quick check said.</summary>
    public Dictionary<string, QaGate> Gates { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many cards the queue keeps at work at once (WOR-0244): 1, one at a time as before; more, and as soon as
    /// any leaves the working state (QA, attention, done, failed or stopped) the next waiting card starts, so the batch stays full.</summary>
    public int Batch { get; set; } = 1;
    public const int MaxBatch = 50;

    [JsonIgnore] public bool HasRun => Started.Count > 0 || Why is not null;

    public bool Has(string id) => Cards.Contains(id, StringComparer.OrdinalIgnoreCase);
    public bool IsStarted(string id) => Started.Contains(id, StringComparer.OrdinalIgnoreCase);
    public bool IsReleased(string id) => Released.Contains(id, StringComparer.OrdinalIgnoreCase);
    public void Release(string id) { if (!IsReleased(id)) Released.Add(id); }
}

/// <summary>What the quick check said about a card in QA (WOR-0212): whether the next card needs its QA passed first,
/// which card that was, and the reason given.</summary>
public sealed record QaGate(bool Blocking, string Next, string Why);

/// <summary>What a queue does next (<see cref="QueueRun.Next"/>).</summary>
public abstract record QueueStep
{
    /// <summary>Start an agent on this card, as Work on does.</summary>
    public sealed record Start(string Id) : QueueStep;
    /// <summary>The card reached QA: ask whether its QA blocks <paramref name="Next"/> before moving on.</summary>
    public sealed record CheckQa(string Id, string Next) : QueueStep;
    /// <summary>Something failed: pause the queue on this card, saying why.</summary>
    public sealed record Pause(string Id, string Why) : QueueStep;
    /// <summary>Nothing to do yet: a card is at work, or the rest wait on blockers.</summary>
    public sealed record Wait(string Why) : QueueStep;
    /// <summary>Paused already (by a failure, by hand, or never run).</summary>
    public sealed record Held(string Why) : QueueStep;
    /// <summary>Every card is done or moved on from.</summary>
    public sealed record Finished : QueueStep;
}

/// <summary>How a queued card's agents stand, as the queue sees it.</summary>
public enum QueueAgent { None, Working, Asking, Failed, Stopped }

/// <summary>The queue's rules (WOR-0212, Philip's picks on 2026-11-25): one card at a time; the next starts once the one
/// before is done, or is in QA and a quick check says nothing waits on that QA; a card waiting on the person (in For Human
/// Attention, or its agent asking) holds its place while the queue carries on; only a failure pauses it (an agent's turn
/// failing, or its agent stopping with the card still being worked on). A card whose blockers are still open is passed
/// over until they clear; one already in QA, attention or done when its turn comes is passed over too.</summary>
public static class QueueRun
{
    /// <summary>The queue's next step, from each card's place on the boards (<paramref name="find"/>, null for a card no
    /// board has) and its agents (<paramref name="agent"/>). Records what it learns in the queue (cards started by hand
    /// are taken as started; finished ones released); the caller saves it.</summary>
    public static QueueStep Next(CardQueue q, Func<string, BoardCard?> find, Func<string, QueueAgent> agent,
        IReadOnlyDictionary<string, BoardCard> index)
    {
        if (q.Paused) return new QueueStep.Held(q.Why ?? "Not running yet: press Run to start it.");
        if (q.Batch > 1) return NextBatch(q, find, agent, index);
        // first the cards already started: one still at work holds the queue
        foreach (var id in q.Started.ToList())
        {
            if (q.IsReleased(id)) continue;
            if (!q.Has(id) || find(id) is not { } c || c.Done) { q.Release(id); continue; }
            var a = agent(id);
            if (a == QueueAgent.Asking || c.InAttention) { q.Release(id); continue; } // waits on the person: holds its place
            if (a == QueueAgent.Working) return new QueueStep.Wait($"{id} is being worked on.");
            if (c.InQa)
            {
                var next = NextCard(q, find, agent, index, adopt: false, out _);
                if (next is null) { q.Release(id); continue; }
                if (q.Gates.TryGetValue(id, out var gate) && gate.Next.Equals(next, StringComparison.OrdinalIgnoreCase))
                {
                    if (!gate.Blocking) { q.Release(id); continue; }
                    return new QueueStep.Wait($"{id} is in QA and {next} waits for it to be done: {gate.Why}");
                }
                return new QueueStep.CheckQa(id, next);
            }
            return a == QueueAgent.Failed
                ? new QueueStep.Pause(id, $"{id}'s agent failed with the card in {c.Column}.")
                : new QueueStep.Pause(id, $"{id}'s agent stopped with the card still in {c.Column}.");
        }
        var start = NextCard(q, find, agent, index, adopt: true, out var waiting);
        if (start is not null)
            // one started by hand meanwhile is taken on as the queue's own: go round again to wait on it
            return q.IsStarted(start) ? Next(q, find, agent, index) : new QueueStep.Start(start);
        return waiting.Count > 0
            ? new QueueStep.Wait("The rest wait on blockers: " + string.Join("; ", waiting.Select(w => $"{w.Id} on {w.On}")) + ".")
            : new QueueStep.Finished();
    }

    /// <summary>A batch queue's next step (WOR-0244): every started card not at work has left the batch (a card in QA,
    /// waiting on the person, done, failed or stopped is moved on from, so a failure pauses nothing and no QA check is
    /// asked), and while fewer than <see cref="CardQueue.Batch"/> are at work the next card free of blockers starts.</summary>
    static QueueStep NextBatch(CardQueue q, Func<string, BoardCard?> find, Func<string, QueueAgent> agent,
        IReadOnlyDictionary<string, BoardCard> index)
    {
        var working = 0;
        foreach (var id in q.Started.ToList())
        {
            if (q.IsReleased(id)) continue;
            if (q.Has(id) && find(id) is { Done: false, InAttention: false, InQa: false } && agent(id) == QueueAgent.Working) working++;
            else q.Release(id);
        }
        if (working >= q.Batch) return new QueueStep.Wait($"{working} of {q.Batch} at work.");
        var start = NextCard(q, find, agent, index, adopt: true, out var waiting);
        if (start is not null)
            return q.IsStarted(start) ? NextBatch(q, find, agent, index) : new QueueStep.Start(start);
        var blocked = waiting.Count > 0 ? "the rest wait on blockers: " + string.Join("; ", waiting.Select(w => $"{w.Id} on {w.On}")) + "." : "no more to start.";
        if (working > 0) return new QueueStep.Wait($"{working} at work; {blocked}");
        return waiting.Count > 0 ? new QueueStep.Wait("The rest wait on blockers: " + string.Join("; ", waiting.Select(w => $"{w.Id} on {w.On}")) + ".") : new QueueStep.Finished();
    }

    /// <summary>The first card in the queue's order still to start: not started, on a board, not done, in QA or waiting on
    /// the person, and with no open blocker. A card already at work (started by hand) is returned and, with
    /// <paramref name="adopt"/>, recorded as started. <paramref name="waiting"/> lists the cards passed over for blockers.</summary>
    static string? NextCard(CardQueue q, Func<string, BoardCard?> find, Func<string, QueueAgent> agent,
        IReadOnlyDictionary<string, BoardCard> index, bool adopt, out List<(string Id, string On)> waiting)
    {
        waiting = [];
        foreach (var id in q.Cards)
        {
            if (q.IsStarted(id) || q.IsReleased(id)) continue; // moved on from: never started again
            if (find(id) is not { } c || c.Done || c.InQa || c.InAttention) continue;
            if (agent(id) is QueueAgent.Working or QueueAgent.Asking)
            {
                if (adopt) q.Started.Add(id);
                return id;
            }
            var open = Blockers.Open(c.Card, index);
            if (open.Count > 0) { waiting.Add((id, string.Join(", ", open.Select(b => b.Id)))); continue; }
            return id;
        }
        return null;
    }

    /// <summary>The queue has started this card: it holds the queue until it finishes.</summary>
    public static void Started(CardQueue q, string id) { if (!q.IsStarted(id)) q.Started.Add(id); }

    /// <summary>Pause on a failure, or by hand (<paramref name="on"/> null).</summary>
    public static void Pause(CardQueue q, string why, string? on = null) { q.Paused = true; q.Why = why; q.PausedOn = on; }

    /// <summary>Run it, or carry on after a pause: a card whose failure paused it is moved on from (it stays on its board
    /// for the person to see to), so the queue goes on to the next. That holds for a card that failed to start as much as
    /// for one whose agent failed: it is marked started as well as released, so it isn't picked again.</summary>
    public static void Resume(CardQueue q)
    {
        if (q.PausedOn is { } failed) { Started(q, failed); q.Release(failed); }
        q.Paused = false; q.Why = null; q.PausedOn = null;
    }

    /// <summary>A card's place in the strip: what the queue has done with it.</summary>
    public static string StateOf(CardQueue q, string id, BoardCard? c, QueueAgent a)
    {
        if (c is null) return "not on a board";
        if (c.Done) return "done";
        if (q.PausedOn?.Equals(id, StringComparison.OrdinalIgnoreCase) == true) return "failed";
        if (a == QueueAgent.Working) return "working";
        if (a == QueueAgent.Asking || c.InAttention) return "waits on you";
        if (c.InQa) return q.Gates.TryGetValue(id, out var g) ? g.Blocking ? "in QA, next waits" : "in QA" : "in QA";
        if (q.IsReleased(id)) return a == QueueAgent.Failed ? "failed" : a == QueueAgent.Stopped ? "stopped" : "moved on";
        return q.IsStarted(id) ? "stopped" : "queued";
    }
}

/// <summary>A queue's order and its dependencies (WOR-0212), worked out from the cards' Blocked by lines with no model:
/// a card comes after every card it waits on, otherwise in the order given (a board's order: backlog, then its columns).</summary>
public static class QueueOrder
{
    /// <summary>The cards still to work: not done, in QA or waiting on the person.</summary>
    public static bool Open(BoardCard c) => !c.Done && !c.InQa && !c.InAttention;

    /// <summary>A queue for a board: its open cards, each with the open cards it waits on from any board of the project,
    /// in dependency order.</summary>
    public static List<string> ForBoard(string board, IReadOnlyList<BoardCard> cards)
    {
        var index = Blockers.Index(cards);
        var ids = cards.Where(c => c.Board.Equals(board, StringComparison.OrdinalIgnoreCase) && Open(c)).Select(c => c.Card.Id);
        return Sort(WithBlockers(ids, index), index);
    }

    /// <summary>The cards given, each preceded by the open cards it waits on (followed through their own Blocked by lines,
    /// across boards), each once, in dependency order. Unknown ids are left out.</summary>
    public static List<string> WithBlockers(IEnumerable<string> ids, IReadOnlyDictionary<string, BoardCard> index)
    {
        var asked = ids.Where(index.ContainsKey).Select(id => index[id].Card.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var askedSet = asked.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var all = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // a blocker that wasn't asked for goes just before the first card that needs it; asked ones keep their own place
        void Pull(BoardCard c)
        {
            foreach (var b in c.Card.BlockedBy)
            {
                if (askedSet.Contains(b) || !index.TryGetValue(b, out var bc) || !Open(bc) || !seen.Add(bc.Card.Id)) continue;
                Pull(bc);
                all.Add(bc.Card.Id);
            }
        }
        foreach (var id in asked)
        {
            Pull(index[id]);
            if (seen.Add(id)) all.Add(id);
        }
        return Sort(all, index);
    }

    /// <summary>The ids in dependency order: each after the ones it waits on among them, else as early as the order given
    /// allows (the first card free to go, each time). Cards in a cycle keep their given order after the rest.</summary>
    public static List<string> Sort(IReadOnlyList<string> ids, IReadOnlyDictionary<string, BoardCard> index)
    {
        var set = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        var waits = ids.ToDictionary(id => id, id => (index.TryGetValue(id, out var c) ? c.Card.BlockedBy : [])
            .Where(b => set.Contains(b) && !b.Equals(id, StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        var done = new List<string>();
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (done.Count < ids.Count)
        {
            var next = ids.FirstOrDefault(id => !placed.Contains(id) && waits[id].All(placed.Contains));
            next ??= ids.First(id => !placed.Contains(id)); // a cycle: take the first left
            placed.Add(next);
            done.Add(next);
        }
        return done;
    }

    /// <summary>Add cards to a queue (WOR-0212): each with the open cards it waits on that the queue hasn't got, at the end,
    /// in dependency order. Says which ids were added.</summary>
    public static List<string> Add(CardQueue q, IEnumerable<string> ids, IReadOnlyDictionary<string, BoardCard> index)
    {
        var added = WithBlockers(ids, index).Where(id => !q.Has(id)).ToList();
        foreach (var id in added) Forget(q, id); // anything left from an earlier time in the queue
        q.Cards.AddRange(added);
        return added;
    }

    /// <summary>Take a card out of a queue, and everything the queue knew of it (started, moved on from, its QA check,
    /// the pause on it), so a card added back runs as a new one. Says whether it was there.</summary>
    public static bool Remove(CardQueue q, string id)
    {
        var gone = q.Cards.RemoveAll(c => c.Equals(id, StringComparison.OrdinalIgnoreCase)) > 0;
        Forget(q, id);
        return gone;
    }

    static void Forget(CardQueue q, string id)
    {
        q.Started.RemoveAll(c => c.Equals(id, StringComparison.OrdinalIgnoreCase));
        q.Released.RemoveAll(c => c.Equals(id, StringComparison.OrdinalIgnoreCase));
        q.Gates.Remove(id);
        foreach (var k in q.Gates.Where(g => g.Value.Next.Equals(id, StringComparison.OrdinalIgnoreCase)).Select(g => g.Key).ToList())
            q.Gates.Remove(k); // a check about this card as the next: asked again if it comes back
        if (q.PausedOn?.Equals(id, StringComparison.OrdinalIgnoreCase) == true) q.PausedOn = null;
    }

    /// <summary>Move a card up (-1) or down (+1) one place.</summary>
    public static bool Shift(CardQueue q, string id, int by)
    {
        var at = q.Cards.FindIndex(c => c.Equals(id, StringComparison.OrdinalIgnoreCase));
        var to = at + by;
        if (at < 0 || to < 0 || to >= q.Cards.Count) return false;
        (q.Cards[at], q.Cards[to]) = (q.Cards[to], q.Cards[at]);
        return true;
    }
}

/// <summary>The quick check when a queued card reaches QA (WOR-0212): Haiku reads the card (its How to check says what
/// the agent couldn't verify) and the next card, and says whether the next needs this QA passed before work on it starts.
/// Anything but a plain answer counts as blocking, so the queue waits for done rather than guessing.</summary>
public static class QueueCheck
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    public static string Prompt(Card card, string cardText, Card next, string nextText) =>
        $"""
        A queue of tickets runs one at a time. Ticket {card.Id} has been worked on and is waiting for a person to check it (QA).
        The next ticket in the queue is {next.Id}. Decide whether work on {next.Id} should wait until {card.Id}'s QA check has passed.

        It is BLOCKING when {next.Id} builds on, changes or depends on what {card.Id} did in a way that a failed QA check would
        undo or change (the same code, screen or behaviour, or something {card.Id}'s How to check says is not yet verified).
        It is NOT BLOCKING when {next.Id} is separate work that would stand whatever the QA check finds.

        Reply with exactly one line: BLOCKING or NOT BLOCKING, then a colon and at most 15 words saying why.

        ===== {card.Id} (in QA) =====
        {Cut(cardText)}

        ===== {next.Id} (next) =====
        {Cut(nextText)}
        """;

    static string Cut(string text) => text.Length <= 6000 ? text : text[..6000] + "\n…";

    // the marks a model wraps words in: markdown bold, italic, code, headings and quotes, and quotation marks
    const string Marks = @"[\s*_`""'“”‘’#>]";

    static readonly Regex Answer = new($@"^(?<verdict>not[\s\-_]*blocking|non[\s\-_]*blocking|blocking)\b{Marks}*(?:[:\-–—.,]{Marks}*)?(?<why>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The verdict and why, or null when the reply isn't one: then the caller treats it as blocking. Takes the
    /// shapes a model drifts into: markdown bold or code, quotes, a leading "Answer:", a full stop, any case, "non-blocking".
    /// Marks are taken off only around the verdict and the reason, so an apostrophe inside the reason ("AGE-0003's") stays.</summary>
    public static (bool Blocking, string Why)? Parse(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return null;
        var line = reply.Replace("\r", "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        line = Regex.Replace(line, $"^{Marks}+", "");
        line = Regex.Replace(line, $@"^(answer|verdict|reply){Marks}*:{Marks}*", "", RegexOptions.IgnoreCase);
        var m = Answer.Match(line);
        if (!m.Success) return null;
        var verdict = m.Groups["verdict"].Value.ToLowerInvariant();
        var why = Regex.Replace(m.Groups["why"].Value, $@"({Marks}|\.)+$", "");
        why = Regex.Replace(why, @"\*\*|`", ""); // bold or code inside the reason
        return (verdict.StartsWith("blocking", StringComparison.Ordinal), why.Length > 0 ? why : "no reason given");
    }
}

/// <summary>A project's queues on disk (WOR-0212): <c>queues.json</c> in its working folder (<see cref="ProjectData"/>).</summary>
public static class QueueStore
{
    public const string FileName = "queues.json";

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    sealed class Saved { public List<CardQueue> Queues { get; set; } = []; }

    public static string PathFor(string projectId, string? instanceFolder = null) =>
        System.IO.Path.Combine(ProjectData.Root(projectId, instanceFolder), FileName);

    /// <summary>The queues in the file, or none when it's missing or unreadable.</summary>
    public static List<CardQueue> Load(string file)
    {
        try
        {
            if (!File.Exists(file)) return [];
            var queues = JsonSerializer.Deserialize<Saved>(File.ReadAllText(file), Options)?.Queues ?? [];
            foreach (var q in queues) q.Gates = new Dictionary<string, QaGate>(q.Gates ?? [], StringComparer.OrdinalIgnoreCase);
            return queues;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    public static void Save(string file, IEnumerable<CardQueue> queues)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(new Saved { Queues = queues.ToList() }, Options));
        File.Move(tmp, file, overwrite: true);
    }

    /// <summary>A name for a new queue: the one asked for, trimmed, else "Queue"; made unique with a number.</summary>
    public static string NameFor(string? wanted, IEnumerable<CardQueue> queues)
    {
        var name = string.IsNullOrWhiteSpace(wanted) ? "Queue" : wanted.Trim();
        var taken = queues.Select(q => q.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name)) return name;
        for (var n = 2; ; n++) if (!taken.Contains($"{name} {n}")) return $"{name} {n}";
    }

    /// <summary>Rename a queue (WOR-0220): the name trimmed, refused when blank or another queue has it (in any case;
    /// the queue's own name in another case is fine). Null when renamed, else why not.</summary>
    public static string? Rename(CardQueue q, string? wanted, IEnumerable<CardQueue> queues)
    {
        var name = wanted?.Trim() ?? "";
        if (name.Length == 0) return "A queue needs a name.";
        if (queues.Any(o => o != q && o.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return $"There's a queue called {name} already.";
        q.Name = name;
        return null;
    }

    /// <summary>Move a queue to another place among the project's queues (WOR-0220: dragged in the board's lanes), the
    /// others closing up around it. Says whether anything moved.</summary>
    public static bool Move(List<CardQueue> queues, int from, int to)
    {
        if (from < 0 || from >= queues.Count) return false;
        to = Math.Clamp(to, 0, queues.Count - 1);
        if (from == to) return false;
        var q = queues[from];
        queues.RemoveAt(from);
        queues.Insert(to, q);
        return true;
    }
}
