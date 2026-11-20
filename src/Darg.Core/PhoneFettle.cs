using System.Text.Json.Nodes;

namespace Darg.Core;

/// <summary>Fettle from the phone (AND-0007): what the phone app and the desktop's fettle-* API commands share.
/// The phone is a thin client (docs/ANDROID.md): it sends what Philip said and what it is showing, and reads the
/// chat back, a question's options included, so they can be tapped. Nothing here needs a UI or a network.</summary>
public static class PhoneFettle
{
    /// <summary>What Philip is looking at on his phone, as the line every Fettle message starts with. The desktop's
    /// line is built from its own screen; this one from what the phone says it is showing.</summary>
    public static string Context(string project, string? section = null, string? card = null, string? doc = null)
    {
        var line = section is { Length: > 0 } ? $"{project} › {section}" : project;
        if (card is { Length: > 0 }) line += $", card {card} open";
        if (doc is { Length: > 0 }) line += $", doc {doc} open";
        return line + ", on his phone";
    }

    /// <summary>Told when a message was dictated: speech-to-text slips are read past, not taken literally.</summary>
    public const string Dictated = "(Dictated on his phone, so read past speech-to-text slips: a misheard word, missing punctuation, \"full stop\" said aloud. If a word matters and you can't tell what he meant, ask with AskUserQuestion.)";

    /// <summary>The message as Fettle gets it: the context line, then (if dictated) the note on slips, then his words.</summary>
    public static string Message(string context, string text, bool voice)
    {
        var nl = Environment.NewLine;
        return $"[Philip is looking at: {context}]{nl}{nl}" + (voice ? Dictated + nl + nl : "") + text;
    }
}

/// <summary>A Fettle chat as the phone sees it: its id (to carry on in), state, and transcript entries from <see cref="From"/>.
/// <see cref="Count"/> is the whole transcript's length: the phone asks again from the first entry it may not have
/// in full (a question still waiting changes when answered), which <see cref="NextFrom"/> says.</summary>
public sealed record FettleChat(string Id, string Name, string Project, string State, bool Busy, bool NeedsYou, int Count, int From, IReadOnlyList<FettleEntry> Items)
{
    public int NextFrom => Items.FirstOrDefault(i => i.Pending)?.Index ?? Count;
    public FettleEntry? Waiting => Items.LastOrDefault(i => i.Pending);

    public static FettleChat Parse(string json)
    {
        var o = JsonNode.Parse(json)!.AsObject();
        var items = (o["items"] as JsonArray ?? []).Select(i => FettleEntry.Parse(i!.AsObject())).ToList();
        return new FettleChat((string?)o["id"] ?? "", (string?)o["name"] ?? "", (string?)o["project"] ?? "", (string?)o["state"] ?? "",
            (bool?)o["busy"] == true, (bool?)o["needsYou"] == true, (int?)o["count"] ?? items.Count, (int?)o["from"] ?? 0, items);
    }
}

/// <summary>One transcript entry. Kind is user, fettle, note, tool, question or permission. A question carries its
/// <see cref="Questions"/>; a permission its tool in <see cref="Text"/> and what it wants in <see cref="Detail"/>.</summary>
public sealed record FettleEntry(int Index, string Kind, string Text, string? Detail, bool Pending, IReadOnlyList<FettleQuestion> Questions)
{
    public static FettleEntry Parse(JsonObject o) => new(
        (int?)o["index"] ?? 0, (string?)o["kind"] ?? "", (string?)o["text"] ?? "", (string?)o["detail"], (bool?)o["pending"] == true,
        (o["questions"] as JsonArray ?? []).Select(q => FettleQuestion.Parse(q!.AsObject())).ToList());

    public JsonObject ToJson()
    {
        var o = new JsonObject { ["index"] = Index, ["kind"] = Kind, ["text"] = Text };
        if (Detail is not null) o["detail"] = Detail;
        if (Kind is "question" or "permission") o["pending"] = Pending;
        if (Questions.Count > 0) o["questions"] = new JsonArray(Questions.Select(q => (JsonNode)q.ToJson()).ToArray());
        return o;
    }
}

/// <summary>One AskUserQuestion question: tap an option's label to answer, or send words of his own.</summary>
public sealed record FettleQuestion(string Header, string Question, bool MultiSelect, IReadOnlyList<FettleOption> Options, string? Chosen)
{
    public static FettleQuestion Parse(JsonObject o) => new((string?)o["header"] ?? "", (string?)o["question"] ?? "", (bool?)o["multiSelect"] == true,
        (o["options"] as JsonArray ?? []).Select(x => new FettleOption((string?)x?["label"] ?? "", (string?)x?["description"] ?? "")).ToList(), (string?)o["chosen"]);

    public JsonObject ToJson() => new()
    {
        ["header"] = Header, ["question"] = Question, ["multiSelect"] = MultiSelect, ["chosen"] = Chosen,
        ["options"] = new JsonArray(Options.Select(x => (JsonNode)new JsonObject { ["label"] = x.Label, ["description"] = x.Description }).ToArray()),
    };
}

public sealed record FettleOption(string Label, string Description);

/// <summary>The phone's side of Fettle chat: the fettle-* commands of the desktop's Darg API, through whatever sends
/// a command (<paramref name="post"/>: a command's name and JSON arguments in, the answer's body out; an error answer
/// throws). Kept to that, so it works with any connection the app makes (AND-0002) and is tested against the real API.</summary>
public sealed class FettleRemote(Func<string, JsonObject, Task<string>> post)
{
    /// <summary>Send what Philip said, with what the phone is showing. No <paramref name="chat"/>: a new chat.
    /// Returns the chat, from the entry he just sent.</summary>
    public async Task<FettleChat> Say(string project, string text, string? chat = null, string? section = null, string? card = null, string? doc = null, bool voice = false)
    {
        var args = new JsonObject { ["project"] = project, ["text"] = text, ["voice"] = voice };
        if (chat is not null) args["chat"] = chat;
        if (section is not null) args["section"] = section;
        if (card is not null) args["card"] = card;
        if (doc is not null) args["doc"] = doc;
        return FettleChat.Parse(await post("fettle-say", args));
    }

    /// <summary>The chat from entry <paramref name="from"/> on (0: all of it).</summary>
    public async Task<FettleChat> Read(string chat, int from = 0) =>
        FettleChat.Parse(await post("fettle", new JsonObject { ["chat"] = chat, ["from"] = from }));

    /// <summary>Answer the question Fettle is waiting on: each question's text to the label tapped (or his own words).</summary>
    public async Task<FettleChat> Answer(string chat, IReadOnlyDictionary<string, string> answers)
    {
        var a = new JsonObject();
        foreach (var (q, label) in answers) a[q] = label;
        return FettleChat.Parse(await post("fettle-answer", new JsonObject { ["chat"] = chat, ["answers"] = a }));
    }

    /// <summary>Allow or deny the tool Fettle is waiting to use.</summary>
    public async Task<FettleChat> Allow(string chat, bool allow) =>
        FettleChat.Parse(await post("fettle-answer", new JsonObject { ["chat"] = chat, ["allow"] = allow }));
}
