using System.Text.Json.Nodes;
using Avalonia.Threading;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>The phone's Needs you screen (AND-0010): <c>needs-you</c> lists what waits on Philip in every project (agents'
/// questions and permission asks, cards in For Human Attention with their decisions) and <c>answer</c> answers an agent's
/// ask as its buttons in the transcript do. A decision is answered with <c>decide</c>, as before.</summary>
public partial class MainViewModel
{
    void RegisterNeedsYouApi(DargApi api)
    {
        api.Add("needs-you", "everything waiting on Philip, every project's (or {project} for one): agents' questions and permission asks, then cards in " +
                             "For Human Attention with the decision each waits on. Each: kind (question, permission, decision), key, project, projectName, title, detail, " +
                             "agent, agentName and at (an ask: give them to answer), card and cardTitle, askedBy, noDecision (a card with none: Approve or Send back), " +
                             "and questions (each header, question, multiSelect, options: label, description, recommended, action).", NeedsYouApi);
        api.Add("answer", "answer an agent's question or permission ask, as its buttons do: {id, at?, answers} for a question (question text to the label picked, " +
                          "labels joined by \", \" for several, or Philip's own words), {id, at?, allow: true|false} for a tool it asks to use. at is the ask's place " +
                          "from needs-you: an ask already answered there is refused rather than answering a newer one. Only with Philip's answer.",
            a => Dispatcher.UIThread.InvokeAsync(() => AnswerAgent(a)).GetTask());
    }

    async Task<string> NeedsYouApi(ApiArgs a)
    {
        // asks are read on the UI thread, where transcripts change; card files off it, as the phone polls this
        var (asks, boards) = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var projects = a.Get("project") is null ? Projects.ToList() : [FindProject(a)];
            var found = new List<NeedsYouItem>();
            foreach (var p in projects)
                foreach (var agent in p.Agents?.Agents ?? [])
                {
                    if (!agent.Open) continue; // a saved agent's old ask has no session to answer it
                    var items = agent.Items.ToList();
                    for (var i = 0; i < items.Count; i++)
                        if (AskOf(p, agent, items[i], i) is { } ask) found.Add(ask);
                }
            return (found, projects.Where(p => p.Config.Boards is not null).Select(p => (p.Config.Id, p.Name, Boards: p.Config.Boards!)).ToList());
        });
        var read = await Task.Run(() => boards.Select(b => (b.Id, b.Name, Cards: CardIndex.Read(b.Boards))).ToList());
        var decisions = await Task.Run(() => read.SelectMany(b => Decisions(b.Id, b.Name, b.Cards)).ToList());
        var titles = read.ToDictionary(b => b.Id, b => b.Cards.GroupBy(c => c.Card.Id).ToDictionary(g => g.Key, g => g.First().Card.Title));
        var all = asks.Select(x => x.Card is { } c && titles.TryGetValue(x.Project, out var t) && t.TryGetValue(c, out var title) ? x with { CardTitle = title } : x)
                      .Concat(decisions);
        return Json(new JsonArray(NeedsYouItem.Order(all).Select(x => (JsonNode?)x.ToJson()).ToArray()));
    }

    static NeedsYouItem? AskOf(ProjectViewModel p, AgentViewModel agent, TranscriptItem item, int at) => item switch
    {
        QuestionItem { Pending: true } q => new NeedsYouItem(NeedKind.Question, p.Config.Id, p.Name,
            string.Join(" / ", q.Questions.Select(g => g.Question)), "", agent.Id, agent.Name, at, agent.CardId,
            Questions: q.Questions.Select(g => new NeedQuestion(g.Header, g.Question, g.MultiSelect,
                g.Options.Select(o => new NeedOption(o.Label, o.Description)).ToList())).ToList()),
        PermissionItem { Pending: true } perm => new NeedsYouItem(NeedKind.Permission, p.Config.Id, p.Name,
            $"Use {perm.Tool}?", perm.Summary, agent.Id, agent.Name, at, agent.CardId),
        _ => null,
    };

    /// <summary>A project's cards in For Human Attention, each with its open decision, or the board's fallback (Approve or
    /// Send back) for one that has none, as the card panel shows it.</summary>
    static IEnumerable<NeedsYouItem> Decisions(string project, string name, IReadOnlyList<BoardCard> cards)
    {
        foreach (var c in cards.Where(c => c.InAttention).DistinctBy(c => c.Card.Id))
        {
            CardDecision? d;
            try { d = CardDecision.Read(File.ReadAllText(c.File)) is { Valid: true } valid ? valid : null; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { d = null; }
            yield return NeedsYouItem.ForDecision(project, name, c.Card, d ?? BoardViewModel.Fallback(c.Card), d is null);
        }
    }

    string AnswerAgent(ApiArgs a)
    {
        var (_, agent) = FindAgent(a.Need("id"));
        TranscriptItem waiting;
        if (int.TryParse(a.Get("at"), out var at))
        {
            if (at < 0 || at >= agent.Items.Count || agent.Items[at] is not (QuestionItem or PermissionItem) || agent.Items[at] is not IPending item)
                throw new ApiError($"\"{agent.Name}\" has no ask at {at}.");
            if (!item.Pending) throw new ApiError($"That ask of \"{agent.Name}\" was already answered.");
            waiting = agent.Items[at];
        }
        else waiting = agent.Items.OfType<IPending>().LastOrDefault(x => x.Pending) as TranscriptItem
                       ?? throw new ApiError($"\"{agent.Name}\" isn't waiting on anything.");
        if (!agent.Open) throw new ApiError($"\"{agent.Name}\" has no session running to take the answer; send it a message to carry on.");
        AnswerPending(waiting, a);
        return Json(new JsonObject { ["id"] = agent.Id, ["name"] = agent.Name, ["state"] = agent.StateText, ["answered"] = $"Answered \"{agent.Name}\"." });
    }

    /// <summary>Answers a question (<c>answers</c>: question text to label) or a permission ask (<c>allow</c>), as their
    /// buttons do: Fettle's chats from the phone and project agents both come here.</summary>
    static void AnswerPending(TranscriptItem waiting, ApiArgs a)
    {
        switch (waiting)
        {
            case QuestionItem q:
                JsonObject? given;
                try { given = a.Get("answers") is { } json ? JsonNode.Parse(json) as JsonObject : null; }
                catch (System.Text.Json.JsonException) { given = null; }
                if (given is null || given.Count == 0) throw new ApiError("Missing \"answers\": each question's text to the label picked.");
                // every answer found before any is given: a part-answered ask would send what it has at the last pick
                var picks = q.Questions.Select(g => (g.Question, Answer: (string?)given[g.Question]
                                 ?? (q.Questions.Count == 1 && given.Count == 1 ? (string?)given.First().Value : null) // one question: its text needn't match exactly
                                 ?? throw new ApiError($"No answer for \"{g.Question}\"."))).ToList();
                foreach (var (question, answer) in picks) q.Pick(question, answer);
                break;
            case PermissionItem p:
                var allow = a.Need("allow") is "true" or "1" or "yes";
                (allow ? p.AllowCommand : p.DenyCommand).Execute(null);
                break;
        }
    }
}
