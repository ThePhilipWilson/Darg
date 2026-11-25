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
        Add("queue-new", "make a named queue, as the board's + Queue does: {project, name?, board?, cards?}. Filled with the board's open cards (default " +
                         "the board showing), each after the cards it waits on (its Blocked by line, pulled in from other boards too), or with the cards " +
                         "given and their open blockers. It waits for queue-run. Returns it.", NewQueue);
        Add("queue-add", "add cards to a queue, each with the open cards it waits on ahead of it: {project, queue, cards} (a list, or ids separated by spaces).",
            a => { var (qs, q) = Queue(a); var added = qs.Add(q, a.List("cards")); return added.Count == 0 ? "Added nothing: in the queue already, or no board has them." : $"Added {string.Join(", ", added)} to {q.Name}."; });
        Add("queue-remove", "take cards out of a queue (they stay on their boards): {project, queue, cards}.",
            a => { var (qs, q) = Queue(a); var gone = a.List("cards").Where(id => qs.Remove(q, id)).ToList(); return gone.Count == 0 ? $"None of those is in {q.Name}." : $"Took {string.Join(", ", gone)} out of {q.Name}."; });
        Add("queue-run", "run a queue, or carry on after a pause (moving on past the card that failed), as its ▶ Run / Resume does; {pause: true} pauses it: " +
                         "{project, queue, pause?}. Running starts agents: only when Philip asks.", RunQueue);
        Add("queue-delete", "delete a queue (its cards stay on their boards, agents at work carry on): {project, queue}.",
            a => { var (qs, q) = Queue(a); qs.Delete(q); return $"Deleted {q.Name}."; });
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
        try { return Json(QueueJson(qs, qs.Make(a.Get("name") ?? board, board, cards))); }
        catch (InvalidOperationException e) { throw new ApiError(e.Message); }
    }

    string RunQueue(ApiArgs a)
    {
        var (qs, q) = Queue(a);
        var pause = a.Get("pause") is { } f && (f.Equals("true", StringComparison.OrdinalIgnoreCase) || f is "1" or "yes");
        if (pause == q.Paused) return pause ? $"{q.Name} is paused already: {q.Why}" : $"{q.Name} is running already.";
        qs.Toggle(q);
        return pause ? $"Paused {q.Name}: what's at work carries on, nothing new starts." : $"{q.Name} is running.";
    }

    static JsonArray QueuesJson(QueuesViewModel qs) => [.. qs.All.Select(q => (JsonNode)QueueJson(qs, q))];

    static JsonObject QueueJson(QueuesViewModel qs, CardQueue q) => new()
    {
        ["name"] = q.Name,
        ["paused"] = q.Paused,
        ["why"] = q.Why,
        ["doing"] = qs.Queues.FirstOrDefault(r => r.Queue == q)?.Doing,
        ["cards"] = new JsonArray([.. q.Cards.Select(id => (JsonNode)new JsonObject
        {
            ["id"] = id,
            ["title"] = qs.Card(id)?.Card.Title,
            ["state"] = QueueRun.StateOf(q, id, qs.Card(id), qs.AgentOf(id)),
        })]),
    };
}
