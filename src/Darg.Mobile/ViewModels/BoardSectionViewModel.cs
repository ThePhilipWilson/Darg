using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Darg.Mobile.ViewModels;

/// <summary>A project's board on the phone (AND-0004): one list at a time, swiped between (or picked from the strip of
/// list names), a card tapped open to read, moved by a move sheet (a long press on its tile, or Move in the open card),
/// and a quick-add at the foot of each list. Everything goes through the desktop's API: <c>boards</c>, <c>card</c>,
/// <c>move-card</c> and <c>new-card</c> with a board and column. Triage is the point: the backlog shows first.</summary>
public sealed partial class BoardSectionViewModel : ObservableObject
{
    /// <summary>Runs a desktop API command (<see cref="DargClient.Call"/>; a test passes the app's API straight in).</summary>
    public delegate Task<JsonNode?> ApiCall(string command, object? args);

    readonly ApiCall _call;
    readonly string _project;
    List<JsonObject> _boards = [];

    public ObservableCollection<string> Boards { get; } = [];
    public ObservableCollection<PhoneList> Lists { get; } = [];

    [ObservableProperty] string? _board;
    [ObservableProperty] int _listIndex = -1;
    [ObservableProperty] PhoneList? _current;
    [ObservableProperty] string _status = "";
    [ObservableProperty] bool _bad;
    [ObservableProperty] bool _busy;

    /// <summary>The card open over the board, or null.</summary>
    [ObservableProperty] PhoneCardDetail? _open;

    /// <summary>The card whose move sheet is up, or null; <see cref="MoveTargets"/> are its board's other lists.</summary>
    [ObservableProperty] PhoneCard? _moving;
    public ObservableCollection<PhoneList> MoveTargets { get; } = [];

    /// <summary>What's typed in the quick-add at the foot of the list showing.</summary>
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(QuickAddCommand))] string _quickTitle = "";

    public bool HasBoards => Boards.Count > 1;
    public bool SheetUp => Moving is not null;

    public BoardSectionViewModel(ApiCall call, string project)
    {
        _call = call;
        _project = project;
    }

    /// <summary>Read the boards again, keeping the board and list showing where they still exist.</summary>
    [RelayCommand]
    public async Task Refresh()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            var reply = await _call("boards", new { project = _project });
            _boards = (reply as JsonArray ?? []).OfType<JsonObject>().ToList();
            var names = _boards.Select(b => (string?)b["name"] ?? "").ToList();
            Sync(Boards, names);
            OnPropertyChanged(nameof(HasBoards));
            var keep = Board is { } b && names.Contains(b) ? b : names.FirstOrDefault(n => _boards.Any(x => (string?)x["name"] == n && (bool?)x["inbox"] == true)) ?? names.FirstOrDefault();
            if (keep != Board) Board = keep; // OnBoardChanged fills the lists
            else Fill();
            if (_boards.Count == 0) Say("This project has no boards.");
            else if (Bad) Say(""); // the last error is over; a "Moved T-1 to Done" stays
        }
        catch (DargClientException e) { Say(e.Message, bad: true); }
        finally { Busy = false; }
    }

    partial void OnBoardChanged(string? value) => Fill(fresh: true);

    /// <summary>Show the picked board's lists: the same list as before when it's still there, else the backlog (or the first with cards).</summary>
    void Fill(bool fresh = false)
    {
        var board = _boards.FirstOrDefault(b => (string?)b["name"] == Board);
        var was = fresh ? null : Current?.Id;
        var lists = (board?["lists"] as JsonArray ?? []).OfType<JsonObject>().Select(PhoneList.From).ToList();
        // update in place where the list is the same, so a swipe position and scroll survive a refresh
        if (Lists.Select(l => l.Id).SequenceEqual(lists.Select(l => l.Id)))
            for (var i = 0; i < lists.Count; i++) Lists[i].Take(lists[i]);
        else
        {
            Lists.Clear();
            foreach (var l in lists) Lists.Add(l);
        }
        var at = was is null ? -1 : Lists.ToList().FindIndex(l => l.Id == was);
        if (at < 0) at = Lists.ToList().FindIndex(l => l.Kind == "backlog" && l.Cards.Count > 0);
        if (at < 0) at = Lists.ToList().FindIndex(l => l.Cards.Count > 0);
        if (at < 0) at = Lists.Count > 0 ? 0 : -1;
        if (ListIndex == at) OnListIndexChanged(at);
        else ListIndex = at;
    }

    partial void OnListIndexChanged(int value)
    {
        Current = value >= 0 && value < Lists.Count ? Lists[value] : null;
        foreach (var l in Lists) l.On = l == Current;
    }

    [RelayCommand]
    void ShowList(PhoneList list) => ListIndex = Lists.IndexOf(list);

    [RelayCommand]
    void Next() { if (ListIndex < Lists.Count - 1) ListIndex++; }

    [RelayCommand]
    void Previous() { if (ListIndex > 0) ListIndex--; }

    /// <summary>Open a card to read: its whole text from the desktop, the header lines left out.</summary>
    [RelayCommand]
    public async Task OpenCard(PhoneCard card)
    {
        if (Moving is not null) return; // the tap ending a long press that put the move sheet up
        Open = new PhoneCardDetail(card, Current?.Name ?? "");
        try
        {
            if (await _call("card", new { project = _project, id = card.Id }) is JsonObject full) Open?.Fill(full);
        }
        catch (DargClientException e) { if (Open?.Card == card) Open.Body = e.Message; }
    }

    [RelayCommand]
    void CloseCard() => Open = null;

    /// <summary>Put up the move sheet for a card: every other list of its board, as big buttons.</summary>
    [RelayCommand]
    public void StartMove(PhoneCard card)
    {
        Moving = card;
        Sync(MoveTargets, Lists.Where(l => !l.Cards.Any(c => c.Id == card.Id)).ToList());
    }

    partial void OnMovingChanged(PhoneCard? value) => OnPropertyChanged(nameof(SheetUp));

    [RelayCommand]
    void CancelMove() => Moving = null;

    /// <summary>Move the card on the sheet to a list; the board reads again so the phone shows what the files now say.</summary>
    [RelayCommand]
    public async Task MoveTo(PhoneList list)
    {
        if (Moving is not { } card) return;
        Moving = null;
        try
        {
            await _call("move-card", new { project = _project, card = card.Id, column = list.Id });
            Say($"Moved {card.Id} to {list.Name}.", bad: false);
            if (Open?.Card.Id == card.Id) Open.Column = list.Name;
        }
        catch (DargClientException e) { Say(e.Message, bad: true); return; }
        await Refresh();
    }

    bool CanQuickAdd() => QuickTitle.Trim().Length > 0 && Current is not null && Board is not null;

    /// <summary>Make a card with the typed title at the end of the list showing, on this board.</summary>
    [RelayCommand(CanExecute = nameof(CanQuickAdd))]
    public async Task QuickAdd()
    {
        if (Current is not { } list || Board is not { } board) return;
        var title = QuickTitle.Trim();
        if (title.Length == 0) return;
        try
        {
            var reply = await _call("new-card", new { project = _project, title, board, column = list.Id });
            QuickTitle = "";
            Say($"Added {(string?)reply?["id"] ?? "the card"} to {list.Name}.", bad: false);
        }
        catch (DargClientException e) { Say(e.Message, bad: true); return; }
        await Refresh();
    }

    partial void OnCurrentChanged(PhoneList? value) => QuickAddCommand.NotifyCanExecuteChanged();

    /// <summary>Android's back: the move sheet goes first, then the open card; false when neither is up, so the page goes.</summary>
    public bool HandleBack()
    {
        if (Moving is not null) { Moving = null; return true; }
        if (Open is not null) { Open = null; return true; }
        return false;
    }

    void Say(string text, bool bad = false) { Status = text; Bad = bad; }

    static void Sync<T>(ObservableCollection<T> shown, IReadOnlyList<T> want)
    {
        if (shown.SequenceEqual(want)) return;
        shown.Clear();
        foreach (var x in want) shown.Add(x);
    }
}

/// <summary>A list of the board (a page of the swipe), with its cards. <see cref="On"/> lights its name in the strip.</summary>
public sealed partial class PhoneList : ObservableObject
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public ObservableCollection<PhoneCard> Cards { get; } = [];

    [ObservableProperty] bool _on;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Label))] int _count;

    /// <summary>Its name and card count, as the strip shows it.</summary>
    public string Label => $"{Name} {Count}";
    public bool Empty => Cards.Count == 0;

    public static PhoneList From(JsonObject o)
    {
        var l = new PhoneList { Id = (string?)o["id"] ?? "", Name = (string?)o["name"] ?? "", Kind = (string?)o["kind"] ?? "" };
        foreach (var c in (o["cards"] as JsonArray ?? []).OfType<JsonObject>()) l.Cards.Add(PhoneCard.From(c));
        l.Count = l.Cards.Count;
        return l;
    }

    /// <summary>Take a fresher read of the same list, changing only the cards that differ.</summary>
    public void Take(PhoneList fresh)
    {
        var want = fresh.Cards.ToList();
        for (var i = Cards.Count - 1; i >= 0; i--)
            if (!want.Any(w => w == Cards[i])) Cards.RemoveAt(i);
        for (var i = 0; i < want.Count; i++)
        {
            if (i < Cards.Count && Cards[i] == want[i]) continue;
            var old = Cards.IndexOf(want[i]);
            if (old >= 0) Cards.Move(old, i); else Cards.Insert(i, want[i]);
        }
        Count = Cards.Count;
        OnPropertyChanged(nameof(Empty));
    }
}

/// <summary>A card's tile: id, title, type, labels, the ticket-quality star (0 to 3), the agent on it, if any, and its
/// blockers still showing (WOR-0172's chips, worded as the desktop's). A record, so a refresh that brings the same card
/// back changes nothing on screen.</summary>
public sealed record PhoneCard(string Id, string Title, string Type, string Labels, string Priority, int Quality, string Agent, string Blockers = "")
{
    public bool HasBlockers => Blockers.Length > 0;

    public bool IsBug => Type.Equals("Bug", StringComparison.OrdinalIgnoreCase);
    public bool IsStory => Type.Equals("Story", StringComparison.OrdinalIgnoreCase);
    public bool HasLabels => Labels.Length > 0;
    public bool HasAgent => Agent.Length > 0;

    /// <summary>The line under the title: type, priority and labels, as far as there are any.</summary>
    public string Meta => string.Join(" · ", new[] { Type, Priority, Labels }.Where(s => s.Length > 0));

    public bool IsFair => Quality == 2;
    public bool IsGood => Quality >= 3;

    /// <summary>The star out of 3 the desktop draws beside the id: "★2".</summary>
    public string Star => $"★{Quality}";

    public static PhoneCard From(JsonObject c) => new(
        (string?)c["id"] ?? "", (string?)c["title"] ?? "", (string?)c["type"] ?? "",
        string.Join(", ", (c["labels"] as JsonArray ?? []).Select(x => x?.ToString() ?? "").Where(x => x.Length > 0)),
        (string?)c["priority"] ?? "", (int?)c["quality"] ?? 0,
        c["agent"] is JsonObject a ? (string?)a["state"] ?? "agent" : "",
        string.Join(" · ", (c["blockedBy"] as JsonArray ?? []).OfType<JsonObject>().Select(Chip).OfType<string>()));

    /// <summary>A blocker's chip as the desktop words it (<c>BlockerState.Chip</c>), or null when it has cleared.</summary>
    static string? Chip(JsonObject b)
    {
        var id = (string?)b["id"] ?? "";
        bool found = (bool?)b["found"] ?? false, cleared = (bool?)b["cleared"] ?? false, cycle = (bool?)b["cycle"] ?? false;
        if (cleared && found && !cycle) return null;
        return !found ? $"{id}: no such card" : cycle ? $"{id}: a cycle" : $"waits on {id}";
    }
}

/// <summary>A card open to read: its heading at once, its text once the desktop sends it.</summary>
public sealed partial class PhoneCardDetail(PhoneCard card, string column) : ObservableObject
{
    public PhoneCard Card { get; } = card;
    public string Heading => $"{Card.Id}: {Card.Title}";

    [ObservableProperty] string _column = column;
    [ObservableProperty] string _body = "Loading…";
    [ObservableProperty] string _decision = "";

    public bool HasDecision => Decision.Length > 0;

    internal void Fill(JsonObject full)
    {
        Body = BodyOf((string?)full["text"] ?? "");
        if (full["decision"] is JsonObject d)
        {
            var options = (d["options"] as JsonArray ?? []).OfType<JsonObject>()
                .Select(o => $"• {(string?)o["label"]}{((bool?)o["recommended"] == true ? " (recommended)" : "")}: {(string?)o["means"]}");
            Decision = $"{(string?)d["question"]}\n" + string.Join("\n", options);
            OnPropertyChanged(nameof(HasDecision));
        }
    }

    /// <summary>The card file without its # heading and its "- **Field:** value" lines, which the phone shows apart:
    /// what's left is the description and the sections after it, as written.</summary>
    public static string BodyOf(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        var start = lines.FindIndex(l => l.StartsWith("## ", StringComparison.Ordinal));
        if (start < 0) start = lines.FindIndex(l => l.Length > 0 && !l.StartsWith("# ", StringComparison.Ordinal) && !l.StartsWith("- **", StringComparison.Ordinal));
        if (start < 0) return "";
        var body = string.Join("\n", lines.Skip(start)).Trim();
        if (body == "## Description" || body.StartsWith("## Description\n", StringComparison.Ordinal)) body = body["## Description".Length..].TrimStart('\n');
        return body.Length > 0 ? body : "(no description)";
    }
}
