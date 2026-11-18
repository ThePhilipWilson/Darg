using System.Text.Json.Nodes;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>The API's card reads (WOR-0140): <c>cards</c> lists a project's cards across its boards with the agent on
/// each, <c>card</c> gives one card whole (text, decision, QA notes, reports, agents). Fettle used to answer board
/// questions by grepping card files and joining them against <c>agents</c> by hand.</summary>
public partial class MainViewModel
{
    const string CardsUsage = "a project's cards across all its boards: {project, column?, label?, text?, agent?}. column is a column's name, or qa, attention " +
        "or done (each board's own; done takes in Completed); label a label; text words in the id, title or description; agent none (no live agent on it), " +
        "live, or any (some agent, finished ones included). Each: id, title, board, column, type, labels, file, agent (the live one: id, state, purpose, or null) " +
        "and agents (every agent started on it, live or not). E.g. {project, column: qa, agent: none} or {project, column: done, agent: any}.";

    const string CardUsage = "one card whole: {project, id}. What cards gives, plus its text (the file), decision (the open Decision it waits on, or null), " +
        "qaNotes, the reports filed for it (archived ones too) and every agent on it with what it's doing.";

    string Cards(ApiArgs a)
    {
        var p = FindProject(a);
        if (p.Config.Boards is null) throw new ApiError($"{p.Name} has no boards.");
        var agentFilter = a.Get("agent")?.Trim().ToLowerInvariant();
        if (agentFilter is not (null or "" or "none" or "live" or "any"))
            throw new ApiError($"agent is none, live or any, not \"{a.Get("agent")}\".");
        var column = a.Get("column");
        var all = CardIndex.Read(p.Config.Boards);
        var cards = CardIndex.Filter(all, column, a.Get("label"), a.Get("text")).ToList();
        if (!string.IsNullOrWhiteSpace(column) && cards.Count == 0 && !all.Any(c => CardIndex.InColumn(c, column)))
        {
            var names = all.Select(c => c.Column).Distinct(StringComparer.OrdinalIgnoreCase);
            throw new ApiError($"No column \"{column}\" on {p.Name}'s boards. Columns: {string.Join(", ", names.Concat(CardIndex.Kinds))}.");
        }
        var list = new JsonArray();
        foreach (var c in cards)
        {
            var on = AgentsOn(p, c.Card.Id);
            var live = on.FirstOrDefault(x => x.IsLiveOnCard);
            var keep = agentFilter switch { "none" => live is null, "live" => live is not null, "any" => on.Count > 0, _ => true };
            if (!keep) continue;
            var row = CardJson(c);
            row["agent"] = live is null ? null : new JsonObject { ["id"] = live.Id, ["state"] = live.StateText, ["purpose"] = live.Purpose };
            row["agents"] = new JsonArray(on.Select(x => (JsonNode?)ShortAgent(x)).ToArray());
            list.Add(row);
        }
        return Json(list);
    }

    string OneCard(ApiArgs a)
    {
        var p = FindProject(a);
        if (p.Config.Boards is null) throw new ApiError($"{p.Name} has no boards.");
        var id = a.Need("id");
        if (CardIndex.Find(p.Config.Boards, id) is not { } c) throw new ApiError($"No card {id} on {p.Name}'s boards.");
        string text;
        try { text = File.ReadAllText(c.File); }
        catch (IOException e) { throw new ApiError($"Couldn't read {c.File}: {e.Message}"); }
        var on = AgentsOn(p, c.Card.Id);
        var live = on.FirstOrDefault(x => x.IsLiveOnCard);
        var row = CardJson(c);
        row["status"] = c.Card.Status;
        row["priority"] = c.Card.Priority;
        row["agent"] = live is null ? null : new JsonObject { ["id"] = live.Id, ["state"] = live.StateText, ["purpose"] = live.Purpose };
        row["agents"] = new JsonArray(on.Select(x =>
        {
            var j = ShortAgent(x);
            j["name"] = x.Name; j["doing"] = x.Activity; j["open"] = x.Open; j["flow"] = x.Flow?.Status;
            return (JsonNode?)j;
        }).ToArray());
        row["decision"] = CardDecision.Read(text) is { Valid: true } d ? new JsonObject
        {
            ["question"] = d.Question, ["askedBy"] = d.AskedBy, ["context"] = d.Context,
            ["options"] = new JsonArray(d.Options.Select(o => (JsonNode?)new JsonObject
            {
                ["label"] = o.Label, ["recommended"] = o.Recommended, ["action"] = o.Action.ToString().ToLowerInvariant(), ["means"] = o.Description,
            }).ToArray()),
        } : null;
        row["qaNotes"] = CardIndex.QaNotesOf(text);
        row["reports"] = new JsonArray(CardIndex.ReportsFor(ReportStore.FolderFor(p.Config.Id), c.Card.Id).Select(r => (JsonNode?)new JsonObject
        {
            ["file"] = r.File, ["title"] = r.Title, ["date"] = r.Date, ["kind"] = r.Kind.ToString().ToLowerInvariant(),
            ["decision"] = r.Decision.Length > 0 ? r.Decision : null, ["agent"] = r.Agent.Length > 0 ? r.Agent : null,
            ["commits"] = new JsonArray(r.Commits.Select(x => (JsonNode?)x).ToArray()), ["read"] = r.Read,
        }).ToArray());
        row["text"] = text;
        return Json(row);
    }

    static List<AgentViewModel> AgentsOn(ProjectViewModel p, string cardId) =>
        (p.Agents?.Agents ?? []).Where(x => string.Equals(x.CardId, cardId, StringComparison.OrdinalIgnoreCase)).ToList();

    static JsonObject ShortAgent(AgentViewModel x) =>
        new() { ["id"] = x.Id, ["state"] = x.StateText, ["purpose"] = x.Purpose, ["live"] = x.IsLiveOnCard };

    static JsonObject CardJson(BoardCard c) => new()
    {
        ["id"] = c.Card.Id, ["title"] = c.Card.Title, ["board"] = c.Board, ["column"] = c.Column, ["type"] = c.Card.Kind,
        ["labels"] = new JsonArray(c.Card.Labels.Select(l => (JsonNode?)l).ToArray()), ["file"] = c.File,
    };
}
