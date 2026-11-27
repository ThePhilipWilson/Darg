using System.Text.Json.Nodes;
using Avalonia.Threading;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>The API's queue commands (WOR-0212): list a project's queues, make one, add and take out cards, run or
/// pause it, delete it; what the board's queue strip does.</summary>
public partial class MainViewModel
{
    void RegisterQueueApi(DargApi api)
    {
        void Add(string name, string usage, Func<ApiArgs, string> run) =>
            api.Add(name, usage, a => Dispatcher.UIThread.InvokeAsync(() => run(a)).GetTask());
        Add("queues", "a project's card queues (worked one card at a time, the next starting as the one before finishes): {project}. Each: name, " +
                      "paused, why (paused), doing (what its last look said) and cards in order (id, title, state: queued, working, in QA, waits on you, " +
                      "done, failed, stopped, moved on, not on a board).", a => Json(QueuesJson(ProjectQueues(a))));
        Add("queue-new", "make a named queue, as the board's + Queue does: {project, name?, board?, cards?, profile?, flow?}. Filled with the board's open cards (default " +
                         "the board showing), each after the cards it waits on (its Blocked by line, pulled in from other boards too), or with the cards " +
                         "given and their open blockers. profile or flow (by name, not both) has every card it starts worked on that model profile or run through that flow. It waits for queue-run. Returns it.", NewQueue);
        Add("queue-add", "add cards to a queue, each with the open cards it waits on ahead of it: {project, queue, cards} (a list, or ids separated by spaces).",
            a => { var (qs, q) = Queue(a); var added = qs.Add(q, a.List("cards")); return added.Count == 0 ? "Added nothing: in the queue already, or no board has them." : $"Added {string.Join(", ", added)} to {q.Name}."; });
        Add("queue-remove", "take cards out of a queue (they stay on their boards): {project, queue, cards}.",
            a => { var (qs, q) = Queue(a); var gone = a.List("cards").Where(id => qs.Remove(q, id)).ToList(); return gone.Count == 0 ? $"None of those is in {q.Name}." : $"Took {string.Join(", ", gone)} out of {q.Name}."; });
        Add("queue-with", "set what a queue works its cards with, as the box on its lane does: {project, queue, profile?} (a model profile's name) or {project, queue, flow?} (a flow's name); neither goes back to the default agent. Applies to cards it starts from now on.",
            a =>
            {
                var (qs, q) = Queue(a);
                if (qs.SetWith(q, a.Get("profile"), a.Get("flow")) is { } why) throw new ApiError(why);
                return $"{q.Name} works its cards with: {QueueWith.Label(q)}.";
            });
        Add("queue-run", "run a queue, or carry on after a pause (moving on past the card that failed), as its ▶ Run / Resume does; {pause: true} pauses it: " +
                         "{project, queue, pause?}. Running starts agents: only when Philip asks.", RunQueue);
        Add("queue-batch", "set how many cards a queue keeps at work at once: {project, queue, batch} (1 to 50; 1 is one at a time). With more, as soon as one leaves " +
                           "work (QA, attention, done, failed or stopped) the next waiting card with no open blocker starts.",
            a =>
            {
                var (qs, q) = Queue(a);
                if (!int.TryParse(a.Need("batch"), out var n) || n < 1) throw new ApiError("batch is a number from 1.");
                return $"{q.Name} keeps {qs.SetBatch(q, n)} at work at once.";
            });
        Add("queue-delete", "delete a queue: {project, queue, cards?}. Its cards stay on their boards; with cards: true its unfinished cards are " +
                            "deleted too (each to its board's .deleted folder; done ones stay). Agents at work carry on. Deleting cards: only when Philip asks.",
            a =>
            {
                var (qs, q) = Queue(a);
                var withCards = a.Get("cards") is { } c && (c.Equals("true", StringComparison.OrdinalIgnoreCase) || c is "1" or "yes");
                var (deleted, failed) = qs.Delete(q, withCards);
                return $"Deleted {q.Name}" + (deleted.Count > 0 ? $" and its unfinished cards {string.Join(", ", deleted)}" : withCards ? "; it had no unfinished cards" : "")
                       + (failed.Count > 0 ? $"; couldn't delete {string.Join(", ", failed)}" : "") + ".";
            });
        Add("queue-rename", "rename a queue: {project, queue, name}.",
            a => { var (qs, q) = Queue(a); var was = q.Name; return qs.Rename(q, a.Need("name")) is { } why ? throw new ApiError(why) : $"Renamed {was} to {q.Name}."; });
        Add("queue-move", "move a queue among the project's queues, as dragging its lane does: {project, queue, to} (to: its new place, 1 first).",
            a =>
            {
                var (qs, q) = Queue(a);
                if (!int.TryParse(a.Need("to"), out var to) || to < 1) throw new ApiError("to is a place from 1.");
                qs.Move(qs.All.ToList().IndexOf(q), to - 1);
                return $"{q.Name} is queue {qs.All.ToList().IndexOf(q) + 1} of {qs.All.Count}.";
            });
    }

    QueuesViewModel ProjectQueues(ApiArgs a)
    {
        var p = FindProject(a);
        return p.Queues ?? throw new ApiError($"{p.Name} needs a board and agents for queues.");
    }

    (QueuesViewModel, CardQueue) Queue(ApiArgs a)
    {
        var qs = ProjectQueues(a);
        var name = a.Need("queue");
        return (qs, qs.Find(name) ?? throw new ApiError($"No queue \"{name}\". Queues: {(qs.All.Count == 0 ? "none" : string.Join(", ", qs.All.Select(q => q.Name)))}."));
    }

    string NewQueue(ApiArgs a)
    {
        var p = FindProject(a);
        var qs = p.Queues ?? throw new ApiError($"{p.Name} needs a board and agents for queues.");
        var cards = a.Get("cards") is null ? null : a.List("cards");
        var board = cards is { Count: > 0 } ? null : a.Get("board") ?? p.Board?.SelectedBoard;
        try { return Json(QueueJson(qs, qs.Make(a.Get("name") ?? board, board, cards, a.Get("profile"), a.Get("flow")))); }
        catch (InvalidOperationException e) { throw new ApiError(e.Message); }
    }

    string RunQueue(ApiArgs a)
    {
        var (qs, q) = Queue(a);
        var pause = a.Get("pause") is { } f && (f.Equals("true", StringComparison.OrdinalIgnoreCase) || f is "1" or "yes");
        if (pause == q.Paused) return pause ? $"{q.Name} is paused already: {q.Why ?? "it hasn't been run yet."}" : $"{q.Name} is running already.";
        qs.Toggle(q);
        return pause ? $"Paused {q.Name}: what's at work carries on, nothing new starts." : $"{q.Name} is running.";
    }

    static JsonArray QueuesJson(QueuesViewModel qs) => [.. qs.All.Select(q => (JsonNode)QueueJson(qs, q))];

    static JsonObject QueueJson(QueuesViewModel qs, CardQueue q) => new()
    {
        ["name"] = q.Name,
        ["paused"] = q.Paused,
        ["batch"] = q.Batch,
        ["why"] = q.Why,
        ["profile"] = q.Profile,
        ["flow"] = q.Flow,
        ["doing"] = qs.Queues.FirstOrDefault(r => r.Queue == q)?.Doing,
        ["cards"] = new JsonArray([.. q.Cards.Select(id => (JsonNode)new JsonObject
        {
            ["id"] = id,
            ["title"] = qs.Card(id)?.Card.Title,
            ["state"] = QueueRun.StateOf(q, id, qs.Card(id), qs.AgentOf(id)),
        })]),
    };
}
