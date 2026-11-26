using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>A project's card queues (WOR-0212): named lists of cards Darg works one at a time, starting an agent on the
/// next as the one before finishes (<see cref="QueueRun"/> has the rules). Shown as a strip above the board; run from here
/// whatever is on screen. It looks again whenever a board is read, and every few seconds while a queue is running, reading
/// the boards off the UI thread.</summary>
public partial class QueuesViewModel : ObservableObject
{
    readonly ProjectConfig _config;
    readonly AgentsViewModel _agents;
    readonly string _file;
    readonly List<CardQueue> _queues;
    readonly DispatcherTimer _timer;
    readonly HashSet<string> _checking = new(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, BoardCard> _index = new(StringComparer.OrdinalIgnoreCase);
    bool _reading, _again;

    /// <summary>Start an agent on a card as Work on does (the board's <see cref="BoardViewModel.StartQueued"/>): null
    /// when started, else why not.</summary>
    public Func<string, string?>? Start { get; set; }
    /// <summary>The board redraws its tiles (their queue chips and menus, and which cards sit in the lanes).</summary>
    public Action? Changed { get; set; }
    /// <summary>A queued card's full tile from the board on screen (WOR-0220), or null when its board isn't showing.</summary>
    public Func<string, CardViewModel?>? TileFor { get; set; }
    /// <summary>Open a card's panel by id (a plain lane tile, for a card whose board isn't showing).</summary>
    public Action<string>? OpenCard { get; set; }
    /// <summary>Ask the QA check (Haiku by default; tests may swap it).</summary>
    public Func<string, Task<string?>> Ask { get; set; } = prompt => Haiku.Ask(prompt, QueueCheck.Timeout);

    public ObservableCollection<QueueViewModel> Queues { get; } = [];
    public bool HasQueues => Queues.Count > 0;
    public bool ShowsStrip => Queues.Any(q => q.IsShown) || IsMaking;

    /// <summary>The boards on screen (WOR-0239): null while the All board shows (every queue shows), else the names of the
    /// boards drawn. A queue shows only when one of its cards is on a board showing (an empty one, or one whose cards no
    /// board has, shows everywhere: nothing ties it to a board).</summary>
    public Func<IReadOnlyCollection<string>?>? Showing { get; set; }

    internal bool ShowsQueue(CardQueue q)
    {
        if (Showing?.Invoke() is not { } boards || q.Cards.Count == 0) return true;
        var homes = q.Cards.Select(id => Card(id)?.Board).ToList();
        return homes.Any(b => b is null) || homes.Any(b => boards.Contains(b!, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>The boards showing changed: each lane shows or hides.</summary>
    public void ShowingChanged()
    {
        foreach (var r in Queues) r.RefreshShown();
        OnPropertyChanged(nameof(ShowsStrip));
    }

    /// <summary>The + Queue form: a name, filled from <see cref="MakeFrom"/>'s open cards.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowsStrip))] public partial bool IsMaking { get; set; }
    [ObservableProperty] public partial string NewName { get; set; } = "";
    [ObservableProperty] public partial string? MakeFrom { get; set; }
    [ObservableProperty] public partial string Error { get; set; } = "";
    public string MakeHint => MakeFrom is null ? "An empty queue: add cards from their menus." : $"Filled with {MakeFrom}'s open cards, each after what it waits on.";
    partial void OnMakeFromChanged(string? value) => OnPropertyChanged(nameof(MakeHint));

    public QueuesViewModel(ProjectConfig config, AgentsViewModel agents, string? file = null)
    {
        _config = config;
        _agents = agents;
        _file = file ?? QueueStore.PathFor(config.Id);
        _queues = QueueStore.Load(_file);
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (_, _) => Look());
        Show();
        Look();
    }

    bool _closed;

    /// <summary>The project is closing: no more steps, so a running queue starts nothing on a closed project, and the
    /// timer (held by the dispatcher) lets go of this and the project (WOR-0226).</summary>
    public void Close()
    {
        _closed = true;
        _timer.Stop();
    }

    public IReadOnlyList<CardQueue> All => _queues;
    /// <summary>Whether any queue holds the card: then it shows in that queue's lane, not in its board's list.</summary>
    public bool Has(string id) => _queues.Any(q => q.Has(id));
    public CardQueue? Find(string name) => _queues.FirstOrDefault(q => q.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>+ Queue: open the form, named for and filled from the board showing.</summary>
    public void BeginNew(string? board)
    {
        MakeFrom = board;
        NewName = QueueStore.NameFor(board, _queues);
        Error = "";
        IsMaking = true;
    }

    [RelayCommand] void CancelNew() { IsMaking = false; Error = ""; }

    [RelayCommand]
    void MakeNew()
    {
        try { Make(NewName, MakeFrom, null); IsMaking = false; Error = ""; }
        catch (InvalidOperationException e) { Error = e.Message; }
    }

    /// <summary>Make a queue: named (unique), filled from a board's open cards in dependency order, or from the cards given
    /// (each with its open blockers ahead of it). It waits for Run. Says what it made.</summary>
    public CardQueue Make(string? name, string? board, IReadOnlyList<string>? cards)
    {
        var all = CardIndex.Read(_config.Boards);
        var index = Blockers.Index(all);
        var q = new CardQueue { Name = QueueStore.NameFor(name, _queues) };
        if (cards is { Count: > 0 }) QueueOrder.Add(q, cards, index);
        else if (board is not null)
        {
            if (_config.Boards is null || !BoardStore.List(_config.Boards).Contains(board, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"No board {board} in {_config.Name}.");
            q.Cards.AddRange(QueueOrder.ForBoard(board, all));
        }
        _queues.Add(q);
        _index = index;
        Saved();
        return q;
    }

    /// <summary>Add cards to a queue, each with its open blockers ahead of it. Says which went in.</summary>
    public IReadOnlyList<string> Add(CardQueue q, IReadOnlyList<string> ids)
    {
        _index = Blockers.Index(CardIndex.Read(_config.Boards));
        var added = QueueOrder.Add(q, ids, _index);
        Saved();
        return added;
    }

    public bool Remove(CardQueue q, string id)
    {
        var gone = QueueOrder.Remove(q, id);
        if (gone) Saved();
        return gone;
    }

    public void Shift(CardQueue q, string id, int by) { if (QueueOrder.Shift(q, id, by)) Saved(); }

    /// <summary>Delete a queue (WOR-0220). With <paramref name="cards"/>, its unfinished cards go too, each to its board's
    /// .deleted folder as a card's own Delete does (so they can come back); done ones stay as the record. Agents at work
    /// on them carry on. Says which cards were deleted, and any that couldn't be.</summary>
    public (IReadOnlyList<string> Deleted, IReadOnlyList<string> Failed) Delete(CardQueue q, bool cards = false)
    {
        var deleted = new List<string>();
        var failed = new List<string>();
        if (cards && _config.Boards is { } boards)
        {
            var all = CardIndex.Read(boards);
            foreach (var id in q.Cards)
            {
                if (all.FirstOrDefault(c => c.Card.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) is not { Done: false } c) continue;
                try { BoardStore.Delete(Path.Combine(boards, c.Board), c.Card.Id); deleted.Add(c.Card.Id); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException) { failed.Add(c.Card.Id); }
            }
        }
        _queues.Remove(q);
        Saved();
        return (deleted, failed);
    }

    /// <summary>Rename a queue: null when renamed, else why not (blank, or another queue's name).</summary>
    public string? Rename(CardQueue q, string? name)
    {
        if (QueueStore.Rename(q, name, _queues) is { } why) return why;
        Saved();
        return null;
    }

    /// <summary>Move a queue to another place among the lanes (dragged by its grip, or the API's queue-move).</summary>
    public void Move(int from, int to) { if (QueueStore.Move(_queues, from, to)) Saved(); }

    /// <summary>The board has built its tiles again: each lane takes the new ones.</summary>
    public void TilesChanged() { foreach (var row in Queues) row.Refresh(); OnPropertyChanged(nameof(ShowsStrip)); }

    /// <summary>Run, Resume or Pause.</summary>
    public void Toggle(CardQueue q)
    {
        if (q.Paused) QueueRun.Resume(q); else QueueRun.Pause(q, "Paused by you.");
        Saved();
    }

    /// <summary>A change to a queue: kept on disk, shown, and looked at again at once.</summary>
    void Saved()
    {
        try { QueueStore.Save(_file, _queues); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Error = "Couldn't save the queues: " + e.Message; }
        Show();
        Changed?.Invoke();
        Look();
    }

    /// <summary>Look at the queues again: read the boards in the background, then take each running queue's next step.</summary>
    public void Look()
    {
        if (_closed || _queues.Count == 0) { _timer.Stop(); return; }
        if (_reading) { _again = true; return; }
        _reading = true;
        var boards = _config.Boards;
        Task.Run(() => { try { return CardIndex.Read(boards); } catch (Exception) { return []; } })
            .ContinueWith(t => Dispatcher.UIThread.Post(() =>
            {
                _reading = false;
                if (_closed) return;
                Step(t.Result);
                if (_again) { _again = false; Look(); }
            }), TaskScheduler.Default);
    }

    /// <summary>Each running queue's next step, from the boards as read.</summary>
    void Step(List<BoardCard> cards)
    {
        _index = Blockers.Index(cards);
        var dirty = false;
        foreach (var q in _queues.ToList())
        {
            var before = (q.Started.Count, q.Released.Count, q.Paused);
            var step = QueueRun.Next(q, Card, AgentOf, _index);
            switch (step)
            {
                case QueueStep.Start s:
                    if (Start?.Invoke(s.Id) is { } why) QueueRun.Pause(q, $"Couldn't start {s.Id}: {why}.", s.Id);
                    else QueueRun.Started(q, s.Id);
                    break;
                case QueueStep.CheckQa c:
                    Check(q, c.Id, c.Next);
                    break;
                case QueueStep.Pause p:
                    QueueRun.Pause(q, p.Why, p.Id);
                    break;
            }
            Row(q)?.Said(step);
            dirty |= before != (q.Started.Count, q.Released.Count, q.Paused);
        }
        if (dirty)
        {
            try { QueueStore.Save(_file, _queues); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            Changed?.Invoke();
        }
        foreach (var row in Queues) row.Refresh();
        OnPropertyChanged(nameof(ShowsStrip));
        // a running queue is looked at every few seconds as well (a card can move on a board that isn't showing)
        if (_queues.Any(q => !q.Paused)) { if (!_timer.IsEnabled) _timer.Start(); }
        else _timer.Stop();
    }

    public BoardCard? Card(string id) => _index.GetValueOrDefault(id);

    /// <summary>How the card's agents stand: at work or asking (any live one), else its newest one failed or stopped, or none.
    /// An improve agent doesn't count: it fills the card out, it doesn't do it.</summary>
    public QueueAgent AgentOf(string id)
    {
        var on = _agents.Agents.Where(a => a.CardId is { } c && c.Equals(id, StringComparison.OrdinalIgnoreCase) && a.Purpose != AgentPurposes.Improve).ToList();
        if (on.Any(a => a.IsLiveOnCard)) return on.Any(a => a.State == AgentState.NeedsYou) ? QueueAgent.Asking : QueueAgent.Working;
        return on.MaxBy(a => a.Created) is not { } last ? QueueAgent.None : last.EndedInError ? QueueAgent.Failed : QueueAgent.Stopped;
    }

    /// <summary>The quick check on a card that reached QA: does the next card need that QA passed first? Asked once at a
    /// time per card; no clear answer counts as blocking, so the queue waits for done.</summary>
    void Check(CardQueue q, string id, string next)
    {
        if (Card(id) is not { } card || Card(next) is not { } nextCard || !_checking.Add(id)) return;
        string Text(BoardCard c) { try { return File.ReadAllText(c.File); } catch (Exception) { return c.Card.Title + "\n\n" + c.Card.Description; } }
        var prompt = QueueCheck.Prompt(card.Card, Text(card), nextCard.Card, Text(nextCard));
        Ask(prompt).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            _checking.Remove(id);
            var answer = QueueCheck.Parse(t.IsCompletedSuccessfully ? t.Result : null);
            q.Gates[id] = answer is { } a ? new QaGate(a.Blocking, next, a.Why)
                : new QaGate(true, next, "the check gave no clear answer, so it waits for done");
            Saved();
        }), TaskScheduler.Default);
    }

    /// <summary>The tile's chips for a card: "⏵ Travel 3" per queue it's in, with its place.</summary>
    public IReadOnlyList<string> ChipsFor(string id) =>
        _queues.Where(q => q.Has(id)).Select(q => $"⏵ {q.Name} {q.Cards.FindIndex(c => c.Equals(id, StringComparison.OrdinalIgnoreCase)) + 1}").ToList();

    /// <summary>A card's menu entries: add to each queue it isn't in, take out of each it is, or a new queue from it.</summary>
    /// <param name="carried">The cards the menu acts on, read when an entry is chosen: the card, or every picked card when it is one of them (WOR-0241).</param>
    public IEnumerable<MoveTarget> TargetsFor(string id, Func<IReadOnlyList<string>>? carried = null, bool takeOutOnly = false)
    {
        IReadOnlyList<string> Ids() => carried?.Invoke() is { Count: > 0 } c ? c : [id];
        foreach (var q in _queues)
        {
            if (q.Has(id)) yield return new MoveTarget($"⏵ Take out of queue {q.Name}", new RelayCommand(() => { foreach (var x in Ids()) Remove(q, x); }));
            else if (!takeOutOnly) yield return new MoveTarget($"⏵ Add to queue {q.Name}", new RelayCommand(() => DropOn(q, Ids())));
        }
        if (takeOutOnly) yield break;
        yield return new MoveTarget("⏵ New queue from this card", new RelayCommand(() => Make(null, null, Ids())));
    }

    /// <summary>Cards dropped on a queue's lane (or added from a menu): added in order, each with its open blockers, and the lane says what went in.</summary>
    public void DropOn(CardQueue q, IReadOnlyList<string> ids)
    {
        var added = Add(q, ids);
        if (Row(q) is { } row)
            row.Doing = added.Count == 0 ? "Added nothing: those are in the queue already, or no board has them." : $"Added {string.Join(", ", added)}.";
    }

    /// <summary>Cards dragged out of a lane onto a board's list leave every queue they are in (WOR-0241), as ✕ and the menu's Take out do.</summary>
    public void TakeOut(IReadOnlyList<string> ids)
    {
        var gone = new List<string>();
        foreach (var q in _queues.ToList())
            foreach (var id in ids)
                if (Remove(q, id) && !gone.Contains(id)) gone.Add(id);
    }

    /// <summary>A lane's drop target key to its queue.</summary>
    public void DropOnKey(string key, IReadOnlyList<string> ids)
    {
        if (Queues.FirstOrDefault(r => r.Key == key) is { } row) DropOn(row.Queue, ids);
    }

    QueueViewModel? Row(CardQueue q) => Queues.FirstOrDefault(r => r.Queue == q);

    /// <summary>The lanes, one per queue in the queues' order, each kept while its queue is.</summary>
    void Show()
    {
        ListSync.Sync(Queues, _queues.Select(q => Row(q) ?? new QueueViewModel(this, q)).ToList());
        foreach (var r in Queues) r.Refresh();
        OnPropertyChanged(nameof(HasQueues));
        OnPropertyChanged(nameof(ShowsStrip));
    }
}

/// <summary>One queue's lane on the board (WOR-0220): a box holding its cards' tiles in the queue's order (they leave their
/// lists while queued), headed by its name, how far it has got, what it's doing and Run / Pause, with a grip to drag it
/// among the queues and a menu to rename or delete it, keeping its cards or deleting the unfinished ones with it. Folded,
/// it shows the cards' ids in order on one line.</summary>
public partial class QueueViewModel(QueuesViewModel owner, CardQueue queue) : ObservableObject
{
    public CardQueue Queue { get; } = queue;
    public string Name => Queue.Name;
    [ObservableProperty] public partial string Summary { get; set; } = "";
    [ObservableProperty] public partial string Doing { get; set; } = "";
    /// <summary>Open by default: the lane is where its cards are.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Arrow), nameof(IsFolded))] public partial bool IsOpen { get; set; } = true;
    public bool IsFolded => !IsOpen;
    public string Arrow => IsOpen ? "▾" : "▸";
    /// <summary>The lane's drop-target key (DragLift lists are tagged by a string): cards dropped on it join the queue.</summary>
    public string Key => "queue:" + Name;
    /// <summary>The cards' ids in order, for the folded lane: "1 T-0002 → 2 T-0001".</summary>
    [ObservableProperty] public partial string Line { get; set; } = "";
    public ObservableCollection<QueueCardViewModel> Cards { get; } = [];
    public bool IsEmpty => Cards.Count == 0;
    public string RunText => !Queue.Paused ? "⏸ Pause" : Queue.HasRun ? "▶ Resume" : "▶ Run";
    public string RunTip => !Queue.Paused ? "Start nothing new; what's at work carries on"
        : Queue.PausedOn is { } on ? $"Carry on: move on past {on} (it stays on its board for you) and start the next card"
        : "Start the first card; each next one starts once the one before is done, or in QA with nothing waiting on that QA";
    public bool IsPaused => Queue.Paused;
    public bool Failed => Queue.PausedOn is not null;

    // renaming, in place of the name
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowsName))] public partial bool IsRenaming { get; set; }
    public bool ShowsName => !IsRenaming;
    [ObservableProperty] public partial string RenameText { get; set; } = "";
    [ObservableProperty] public partial string RenameError { get; set; } = "";

    // deleting asks first, saying what goes: the queue alone, or its unfinished cards too
    public enum Deleting { No, Queue, WithCards }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ConfirmingDelete), nameof(DeleteQuestion), nameof(DeleteText))]
    public partial Deleting AskingDelete { get; set; }
    public bool ConfirmingDelete => AskingDelete != Deleting.No;
    /// <summary>The cards Delete with its cards would delete: those not done.</summary>
    List<string> Unfinished => Queue.Cards.Where(id => owner.Card(id) is { Done: false }).ToList();
    public string DeleteQuestion => AskingDelete == Deleting.WithCards
        ? Unfinished is { Count: > 0 } u
            ? $"Delete {Name} and its {Plural(u.Count, "unfinished card")} ({string.Join(", ", u)})? They go to their boards' .deleted folders; done cards stay."
            : $"Delete {Name}? It has no unfinished cards, so no card is deleted."
        : $"Delete {Name}? Its cards stay on their boards.";
    public string DeleteText => AskingDelete == Deleting.WithCards ? "Delete queue and cards" : "Delete queue";
    static string Plural(int n, string what) => n == 1 ? $"1 {what}" : $"{n} {what}s";

    /// <summary>What the queue's last step said, in words.</summary>
    public void Said(QueueStep step) => Doing = step switch
    {
        QueueStep.Start s => $"Started {s.Id}.",
        QueueStep.CheckQa c => $"{c.Id} is in QA: checking whether {c.Next} needs that QA passed first…",
        QueueStep.Pause p => "Paused: " + p.Why,
        QueueStep.Wait w => w.Why,
        QueueStep.Held h => h.Why,
        QueueStep.Finished => "Finished: every card is done or moved on from.",
        _ => "",
    };

    /// <summary>Whether this lane shows: its queue has a card on a board that is showing, or the All board does (WOR-0239).</summary>
    [ObservableProperty] public partial bool IsShown { get; set; } = true;
    public void RefreshShown() => IsShown = owner.ShowsQueue(Queue);

    public void Refresh()
    {
        RefreshShown();
        var states = Queue.Cards.Select(id => (Id: id, Card: owner.Card(id), State: QueueRun.StateOf(Queue, id, owner.Card(id), owner.AgentOf(id)))).ToList();
        var done = states.Count(s => s.State == "done");
        Summary = $"{done} of {Queue.Cards.Count} done";
        if (Queue.Paused && Doing.Length == 0) Doing = Queue.Why ?? "Not running yet: press Run to start it.";
        ListSync.Sync(Cards, states.Select((s, i) =>
        {
            var tile = owner.TileFor?.Invoke(s.Id);
            return Cards.FirstOrDefault(c => c.Id == s.Id && c.Matches(i + 1, s.Card, s.State, tile, states.Count))
                ?? new QueueCardViewModel(this, i + 1, s.Id, s.Card, s.State, tile, states.Count);
        }).ToList());
        Line = string.Join("  →  ", Cards.Select(c => $"{c.Place} {c.Id}"));
        foreach (var n in new[] { nameof(Name), nameof(RunText), nameof(RunTip), nameof(IsPaused), nameof(Failed), nameof(IsEmpty), nameof(DeleteQuestion) })
            OnPropertyChanged(n);
    }

    [RelayCommand] void Toggle() { Doing = ""; owner.Toggle(Queue); }
    [RelayCommand] void Open() => IsOpen = !IsOpen;
    [RelayCommand] void AskDelete() => AskingDelete = Deleting.Queue;
    [RelayCommand] void AskDeleteCards() => AskingDelete = Deleting.WithCards;
    [RelayCommand] void CancelDelete() => AskingDelete = Deleting.No;
    [RelayCommand]
    void Delete()
    {
        var (deleted, failed) = owner.Delete(Queue, AskingDelete == Deleting.WithCards);
        AskingDelete = Deleting.No;
        if (failed.Count > 0) owner.Error = $"Deleted {Name}, but couldn't delete {string.Join(", ", failed)}.";
        else if (deleted.Count > 0) owner.Error = "";
    }

    [RelayCommand] void StartRename() { RenameText = Name; RenameError = ""; IsRenaming = true; }
    [RelayCommand] void CancelRename() { IsRenaming = false; RenameError = ""; }
    [RelayCommand]
    void CommitRename()
    {
        if (RenameText.Trim() == Name) { CancelRename(); return; }
        if (owner.Rename(Queue, RenameText) is { } why) { RenameError = why; return; }
        IsRenaming = false;
        RenameError = "";
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Key));
    }

    internal void Up(string id) => owner.Shift(Queue, id, -1);
    internal void Down(string id) => owner.Shift(Queue, id, +1);
    internal void Remove(string id) => owner.Remove(Queue, id);
    internal void OpenCard(string id) => owner.OpenCard?.Invoke(id);
}

/// <summary>A card in a queue's lane (WOR-0220): its place and where it is, above its tile: the board's own tile (Work on,
/// its menu, a click opens it) when its board is on screen, else a plain one that opens it. ← → move it in the queue, ✕
/// takes it out (it goes back to its list).</summary>
public partial class QueueCardViewModel(QueueViewModel queue, int place, string id, BoardCard? card, string state, CardViewModel? tile = null, int count = 0)
{
    public int Place { get; } = place;
    public string Id { get; } = id;
    public string Title => card?.Card.Title ?? "";
    public string State { get; } = state;
    /// <summary>Its board and column, "Main · QA Ready"; or that no board has it.</summary>
    public string Where => card is null ? "not on a board" : $"{card.Board} · {card.Column}";
    public string Line => $"{Place}. {Id}  {Title}";
    public CardViewModel? Tile { get; } = tile;
    public bool HasTile => Tile is not null;
    public bool NoTile => Tile is null;
    public bool IsFirst => Place == 1;
    public bool IsLast => Place >= count;
    public bool IsDone => State == "done";
    public bool IsWorking => State == "working";
    public bool IsFailed => State == "failed";
    /// <summary>Finished with (done, moved on): faded, so what's still to come stands out.</summary>
    public double Fade => State is "done" or "moved on" ? 0.55 : 1;
    public bool Matches(int place, BoardCard? c, string state, CardViewModel? tile, int count) =>
        Place == place && State == state && Title == (c?.Card.Title ?? "") && Where == (c is null ? "not on a board" : $"{c.Board} · {c.Column}")
        && ReferenceEquals(Tile, tile) && IsLast == (place >= count);
    [RelayCommand] void Up() => queue.Up(Id);
    [RelayCommand] void Down() => queue.Down(Id);
    [RelayCommand] void Remove() => queue.Remove(Id);
    [RelayCommand] void Open() => queue.OpenCard(Id);
}
