using System.Text.Json.Nodes;

namespace Darg.Core;

/// <summary>What a <see cref="NeedsYouItem"/> waits on: an agent's AskUserQuestion, an agent's ask to use a tool, or a
/// card in For Human Attention with its decision.</summary>
public enum NeedKind { Question, Permission, Decision }

/// <summary>An option to tap: a question's option, or a decision's (with its action: done, back, stay, or agent).</summary>
public sealed record NeedOption(string Label, string Description, bool Recommended = false, string Action = "");

/// <summary>One question of an ask, or a decision's one question.</summary>
public sealed record NeedQuestion(string Header, string Question, bool MultiSelect, IReadOnlyList<NeedOption> Options);

/// <summary>One thing waiting on Philip, across every project (AND-0010): the API's <c>needs-you</c> lists them and the
/// phone's first screen draws them, each answered with a tap. An ask is answered through <c>answer</c> (its agent and
/// <see cref="At"/>, the ask's place in the transcript, so a tap can't land on a newer ask than the one shown);
/// a decision through <c>decide</c> (its project and card). <see cref="NoDecision"/> marks a card that landed in
/// For Human Attention without one: its options are the board's fallback, Approve or Send back.</summary>
public sealed record NeedsYouItem(
    NeedKind Kind, string Project, string ProjectName, string Title,
    string Detail = "", string? Agent = null, string? AgentName = null, int At = -1,
    string? Card = null, string? CardTitle = null, string AskedBy = "", bool NoDecision = false,
    IReadOnlyList<NeedQuestion>? Questions = null)
{
    public IReadOnlyList<NeedQuestion> Questions { get; init; } = Questions ?? [];

    /// <summary>Stays the same from one read to the next while the thing waits, so a list can be updated in place.</summary>
    public string Key => Kind == NeedKind.Decision ? $"decision:{Project}:{Card}" : $"{Kind.ToString().ToLowerInvariant()}:{Agent}:{At}";

    public JsonObject ToJson() => new()
    {
        ["kind"] = Kind.ToString().ToLowerInvariant(), ["key"] = Key, ["project"] = Project, ["projectName"] = ProjectName,
        ["title"] = Title, ["detail"] = Detail, ["agent"] = Agent, ["agentName"] = AgentName, ["at"] = At < 0 ? null : At,
        ["card"] = Card, ["cardTitle"] = CardTitle, ["askedBy"] = AskedBy, ["noDecision"] = NoDecision,
        ["questions"] = new JsonArray(Questions.Select(q => (JsonNode?)new JsonObject
        {
            ["header"] = q.Header, ["question"] = q.Question, ["multiSelect"] = q.MultiSelect,
            ["options"] = new JsonArray(q.Options.Select(o => (JsonNode?)new JsonObject
            {
                ["label"] = o.Label, ["description"] = o.Description, ["recommended"] = o.Recommended, ["action"] = o.Action,
            }).ToArray()),
        }).ToArray()),
    };

    /// <summary>One entry of the <c>needs-you</c> answer; null when it isn't one (an unknown kind, no project).</summary>
    public static NeedsYouItem? From(JsonObject o)
    {
        if (!Enum.TryParse<NeedKind>(Str(o, "kind"), true, out var kind) || Str(o, "project").Length == 0) return null;
        var questions = (o["questions"] as JsonArray ?? []).OfType<JsonObject>().Select(q => new NeedQuestion(
            Str(q, "header"), Str(q, "question"), Bool(q, "multiSelect"),
            (q["options"] as JsonArray ?? []).OfType<JsonObject>()
                .Select(x => new NeedOption(Str(x, "label"), Str(x, "description"), Bool(x, "recommended"), Str(x, "action")))
                .Where(x => x.Label.Length > 0).ToList())).ToList();
        return new NeedsYouItem(kind, Str(o, "project"), Str(o, "projectName") is { Length: > 0 } n ? n : Str(o, "project"), Str(o, "title"),
            Str(o, "detail"), Opt(o, "agent"), Opt(o, "agentName"), o["at"] is JsonValue v && v.TryGetValue<int>(out var at) ? at : -1,
            Opt(o, "card"), Opt(o, "cardTitle"), Str(o, "askedBy"), Bool(o, "noDecision"), questions);
    }

    /// <summary>The <c>needs-you</c> answer read as a list, in the order the desktop gave (anything not an entry left out).</summary>
    public static IReadOnlyList<NeedsYouItem> List(JsonNode? answer) =>
        answer is JsonObject { } wrap && wrap["items"] is JsonArray inner ? List(inner)
        : answer is JsonArray a ? a.OfType<JsonObject>().Select(From).OfType<NeedsYouItem>().ToList() : [];

    /// <summary>A decision as the phone shows it: its question with the options, recommended first (as the card panel does).</summary>
    public static NeedsYouItem ForDecision(string project, string projectName, Card card, CardDecision decision, bool noDecision) =>
        new(NeedKind.Decision, project, projectName, decision.Question, decision.Context, Card: card.Id, CardTitle: card.Title,
            AskedBy: decision.AskedBy, NoDecision: noDecision,
            Questions: [new NeedQuestion("", decision.Question, false,
                decision.Options.OrderByDescending(o => o.Recommended)
                    .Select(o => new NeedOption(o.Label, o.Description, o.Recommended, o.Action.ToString().ToLowerInvariant())).ToList())]);

    /// <summary>Asks first (an agent is held up mid-turn), then decisions, each in the desktop's project order.</summary>
    public static IReadOnlyList<NeedsYouItem> Order(IEnumerable<NeedsYouItem> items) =>
        items.Select((x, i) => (x, i)).OrderBy(t => t.x.Kind == NeedKind.Decision ? 1 : 0).ThenBy(t => t.i).Select(t => t.x).ToList();

    static string Str(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
    static string? Opt(JsonObject o, string key) => Str(o, key) is { Length: > 0 } s ? s : null;
    static bool Bool(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
}
