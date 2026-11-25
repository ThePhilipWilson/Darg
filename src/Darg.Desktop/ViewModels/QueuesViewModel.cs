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
    /// <summary>The board redraws its tiles (their queue chips and menus).</summary>
    public Action? Changed { get; set; }
    /// <summary>Ask the QA check (Haiku by default; tests may swap it).</summary>
    public Func<string, Task<string?>> Ask { get; set; } = prompt => Haiku.Ask(prompt, QueueCheck.Timeout);

    public ObservableCollection<QueueViewModel> Queues { get; } = [];
    public bool HasQueues => Queues.Count > 0;
    public bool ShowsStrip => HasQueues || IsMaking;

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

    public IReadOnlyList<CardQueue> All => _queues;
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

    public void Delete(CardQueue q) { _queues.Remove(q); Saved(); }

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
        if (_queues.Count == 0) { _timer.Stop(); return; }
        if (_reading) { _again = true; return; }
        _reading = true;
        var boards = _config.Boards;
        Task.Run(() => { try { return CardIndex.Read(boards); } catch (Exception) { return []; } })
            .ContinueWith(t => Dispatcher.UIThread.Post(() =>
            {
                _reading = false;
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
    public IEnumerable<MoveTarget> TargetsFor(string id)
    {
        foreach (var q in _queues)
            yield return q.Has(id)
                ? new MoveTarget($"⏵ Take out of queue {q.Name}", new RelayCommand(() => Remove(q, id)))
                : new MoveTarget($"⏵ Add to queue {q.Name}", new RelayCommand(() => Add(q, [id])));
        yield return new MoveTarget("⏵ New queue from this card", new RelayCommand(() => Make(null, null, [id])));
    }

    QueueViewModel? Row(CardQueue q) => Queues.FirstOrDefault(r => r.Queue == q);

    /// <summary>The strip's rows, one per queue, kept in place.</summary>
    void Show()
    {
        foreach (var r in Queues.Where(r => !_queues.Contains(r.Queue)).ToList()) Queues.Remove(r);
        for (var i = 0; i < _queues.Count; i++)
            if (Row(_queues[i]) is null) Queues.Insert(i, new QueueViewModel(this, _queues[i]));
        foreach (var r in Queues) r.Refresh();
        OnPropertyChanged(nameof(HasQueues));
        OnPropertyChanged(nameof(ShowsStrip));
    }
}

/// <summary>One queue in the strip: its name, how far it has got, what it's doing, Run / Pause, and its cards when opened.</summary>
public partial class QueueViewModel(QueuesViewModel owner, CardQueue queue) : ObservableObject
{
    public CardQueue Queue { get; } = queue;
    public string Name => Queue.Name;
    [ObservableProperty] public partial string Summary { get; set; } = "";
    [ObservableProperty] public partial string Doing { get; set; } = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Arrow))] public partial bool IsOpen { get; set; }
    public string Arrow => IsOpen ? "▾" : "▸";
    [ObservableProperty] public partial string AddText { get; set; } = "";
    [ObservableProperty] public partial bool ConfirmingDelete { get; set; }
    public ObservableCollection<QueueCardViewModel> Cards { get; } = [];
    public string RunText => !Queue.Paused ? "⏸ Pause" : Queue.HasRun ? "▶ Resume" : "▶ Run";
    public string RunTip => !Queue.Paused ? "Start nothing new; what's at work carries on"
        : Queue.PausedOn is { } on ? $"Carry on: move on past {on} (it stays on its board for you) and start the next card"
        : "Start the first card; each next one starts once the one before is done, or in QA with nothing waiting on that QA";
    public bool IsPaused => Queue.Paused;
    public bool Failed => Queue.PausedOn is not null;

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

    public void Refresh()
    {
        var states = Queue.Cards.Select(id => (Id: id, Card: owner.Card(id), State: QueueRun.StateOf(Queue, id, owner.Card(id), owner.AgentOf(id)))).ToList();
        var done = states.Count(s => s.State == "done");
        Summary = $"{done} of {Queue.Cards.Count} done";
        if (Queue.Paused && Doing.Length == 0) Doing = Queue.Why ?? "Not running yet: press Run to start it.";
        ListSync.Sync(Cards, states.Select((s, i) =>
            Cards.FirstOrDefault(c => c.Id == s.Id && c.Matches(i + 1, s.Card, s.State)) ?? new QueueCardViewModel(this, i + 1, s.Id, s.Card, s.State)).ToList());
        OnPropertyChanged(nameof(RunText)); OnPropertyChanged(nameof(RunTip)); OnPropertyChanged(nameof(IsPaused)); OnPropertyChanged(nameof(Failed));
    }

    [RelayCommand] void Toggle() { Doing = ""; owner.Toggle(Queue); }
    [RelayCommand] void Open() => IsOpen = !IsOpen;
    [RelayCommand] void AskDelete() => ConfirmingDelete = true;
    [RelayCommand] void CancelDelete() => ConfirmingDelete = false;
    [RelayCommand] void Delete() => owner.Delete(Queue);

    /// <summary>The box under the cards: ids typed (spaces or commas), each added with its open blockers.</summary>
    [RelayCommand]
    void AddCards()
    {
        var ids = Blockers.ParseIds(AddText);
        if (ids.Count == 0) return;
        var added = owner.Add(Queue, ids);
        Doing = added.Count == 0 ? "Added nothing: those are in the queue already, or no board has them." : $"Added {string.Join(", ", added)}.";
        AddText = "";
    }

    internal void Up(string id) => owner.Shift(Queue, id, -1);
    internal void Down(string id) => owner.Shift(Queue, id, +1);
    internal void Remove(string id) => owner.Remove(Queue, id);
}

/// <summary>A card in an opened queue: its place, id, title and state, with ↑ ↓ ✕.</summary>
public partial class QueueCardViewModel(QueueViewModel queue, int place, string id, BoardCard? card, string state)
{
    public int Place { get; } = place;
    public string Id { get; } = id;
    public string Title => card?.Card.Title ?? "";
    public string State { get; } = state;
    public string Line => $"{Place}. {Id}  {Title}";
    public bool IsDone => State == "done";
    public bool IsWorking => State == "working";
    public bool IsFailed => State == "failed";
    /// <summary>Finished with (done, moved on): faded, so what's still to come stands out.</summary>
    public double Fade => State is "done" or "moved on" ? 0.55 : 1;
    public bool Matches(int place, BoardCard? c, string state) => Place == place && State == state && Title == (c?.Card.Title ?? "");
    [RelayCommand] void Up() => queue.Up(Id);
    [RelayCommand] void Down() => queue.Down(Id);
    [RelayCommand] void Remove() => queue.Remove(Id);
}
