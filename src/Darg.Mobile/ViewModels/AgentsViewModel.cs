using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Mobile.ViewModels;

/// <summary>Something the phone polls while it's on screen: its view starts it when shown and stops it when it goes.</summary>
public interface IPolled
{
    void StartPolling();
    void StopPolling();
}

/// <summary>A project's Agents section on the phone (AND-0005): its agents, needs-you first, each with its state and what
/// it's doing; tapping one opens its transcript. Polls every few seconds while shown.</summary>
public sealed partial class AgentsListViewModel : ObservableObject, IPolled
{
    public static TimeSpan PollEvery { get; set; } = TimeSpan.FromSeconds(3);

    readonly ShellViewModel _shell;
    readonly string _project;
    DispatcherTimer? _timer;

    public ObservableCollection<RemoteAgent> Agents { get; } = [];

    [ObservableProperty] string _status = "Loading…";
    [ObservableProperty] bool _bad;
    bool _busy;

    public AgentsListViewModel(ShellViewModel shell, string projectId)
    {
        _shell = shell;
        _project = projectId;
    }

    [RelayCommand]
    public async Task Refresh()
    {
        if (_shell.Client is not { } client || _busy) return;
        _busy = true;
        try
        {
            var list = RemoteAgent.List(await client.Call("agents", new { project = _project }));
            if (!list.SequenceEqual(Agents))
            {
                Agents.Clear();
                foreach (var a in list) Agents.Add(a);
            }
            Bad = false;
            Status = list.Count == 0 ? "No agents in this project. Start one on the PC, or from a card." : "";
        }
        catch (Exception e) when (e is DargClientException or ApiError) { Bad = true; Status = e.Message; }
        finally { _busy = false; }
    }

    [RelayCommand]
    void Open(RemoteAgent agent)
    {
        var page = new AgentPageViewModel(_shell, agent.Id, agent.Name);
        _shell.Show(page);
        _ = page.Refresh();
    }

    public void StartPolling()
    {
        _ = Refresh();
        _timer ??= new DispatcherTimer(PollEvery, DispatcherPriority.Background, (_, _) => _ = Refresh());
        _timer.Start();
    }

    public void StopPolling() => _timer?.Stop();
}

/// <summary>One agent's transcript on the phone (AND-0005): its state and what it's doing at the top, the conversation
/// below with long replies folded to a screenful and steps as one line each (tap for the detail), and a box to nudge
/// it, mid-turn too. Reads only what changed on each poll (<see cref="AgentFeed"/>).</summary>
public sealed partial class AgentPageViewModel : PageViewModel, IPolled
{
    public static TimeSpan PollEvery { get; set; } = TimeSpan.FromSeconds(2);

    readonly AgentFeed _feed;
    DispatcherTimer? _timer;
    Task? _reading;

    public string Id { get; }
    public string Name { get; }
    public override string Title => Name;

    public ObservableCollection<EntryRow> Entries { get; } = [];

    [ObservableProperty] string _stateLine = "";
    [ObservableProperty] bool _working;
    [ObservableProperty] bool _needsYou;
    [ObservableProperty] string _status = "";
    [ObservableProperty] bool _bad;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SendCommand))] string _compose = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SendCommand))] bool _sending;

    public string SendHint => Working ? "Nudge it (it reads this mid-turn)…" : "Message the agent…";

    public AgentPageViewModel(ShellViewModel shell, string id, string name)
    {
        Id = id;
        Name = name;
        _feed = new AgentFeed((command, args) => shell.Client is { } c ? c.Call(command, args) : throw new DargClientException("Not connected."), id);
    }

    partial void OnWorkingChanged(bool value) => OnPropertyChanged(nameof(SendHint));

    /// <summary>Read what's new. A poll while a read is under way is skipped (the next one catches up).</summary>
    [RelayCommand]
    public Task Refresh() => _reading ?? Read();

    async Task Read()
    {
        var reading = Fetch();
        _reading = reading;
        try { await reading; }
        finally { _reading = null; }
    }

    async Task Fetch()
    {
        try
        {
            var from = await _feed.Refresh();
            // rows before from are as they were; from there on the feed has new or changed entries
            while (Entries.Count > from) Entries.RemoveAt(Entries.Count - 1);
            for (var i = from; i < _feed.Entries.Count; i++) Entries.Add(new EntryRow(_feed.Entries[i]));
            Working = _feed.Working;
            NeedsYou = _feed.NeedsYou;
            StateLine = _feed.Activity.Length > 0 && Working ? $"{_feed.StateText} · {_feed.Activity}" : _feed.StateText;
            Bad = false;
            Status = NeedsYou ? "It's waiting on a question or a permission: answer it on the PC." : "";
        }
        catch (Exception e) when (e is DargClientException or ApiError) { Bad = true; Status = e.Message; }
    }

    bool CanSend() => !Sending && Compose.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanSend))]
    public async Task Send()
    {
        var text = Compose.Trim();
        Sending = true;
        try
        {
            await _feed.Send(text);
            Compose = "";
            Bad = false;
            Status = "";
            // a read under way started before the message went: wait it out, then read again so the message shows now
            if (_reading is { } running) await running;
            await Read();
        }
        catch (Exception e) when (e is DargClientException or ApiError) { Bad = true; Status = e.Message; }
        finally { Sending = false; }
    }

    public void StartPolling()
    {
        _ = Refresh();
        _timer ??= new DispatcherTimer(PollEvery, DispatcherPriority.Background, (_, _) => _ = Refresh());
        _timer.Start();
    }

    public void StopPolling() => _timer?.Stop();
}

/// <summary>A transcript entry as the phone shows it: what's said in full up to a screenful (Show more for the rest),
/// a step as one line with its result or diff a tap away.</summary>
public sealed partial class EntryRow : ObservableObject
{
    public TranscriptEntry Entry { get; }
    readonly string _detail;

    public EntryRow(TranscriptEntry entry)
    {
        Entry = entry;
        _detail = entry.Kind == "tool"
            ? string.Join("\n", new[]
            {
                entry.Removed is null ? null : "− " + entry.Removed.Replace("\n", "\n− "),
                entry.Added is null ? null : "+ " + entry.Added.Replace("\n", "\n+ "),
                entry.Preview, entry.Result,
            }.Where(s => !string.IsNullOrEmpty(s)))
            : entry.Text;
        Hidden = TranscriptEntry.FoldText(_detail).Hidden;
    }

    public bool IsUser => Entry.Kind == "user";
    public bool IsAgent => Entry.Kind == "agent";
    public bool IsNote => Entry.Kind == "note";
    public bool IsStep => Entry.Kind == "tool";
    public bool IsAsk => Entry.Kind is "permission" or "question";
    public bool IsBad => Entry.Error;

    /// <summary>A step's line; an ask's heading.</summary>
    public string Line => IsStep ? Entry.StepLine
        : Entry.Kind == "permission" ? $"{(Entry.Pending ? "Asks to use" : "Asked to use")} {Entry.Tool}{(Entry.Result is { } r ? $" · {r}" : "")}"
        : Entry.Pending ? "Asks you" : "Asked you";

    /// <summary>Lines left out of <see cref="Text"/> while folded.</summary>
    public int Hidden { get; }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Text), nameof(MoreLabel), nameof(ShowText))] bool _expanded;

    /// <summary>The entry's text, folded to a screenful until opened; a step's result or diff.</summary>
    public string Text => Expanded || Hidden == 0 ? _detail : TranscriptEntry.FoldText(_detail).Shown;
    /// <summary>The text below the line: not for what Philip sent (its bubble shows it), a step's only once opened.</summary>
    public bool ShowText => _detail.Length > 0 && !IsUser && (!IsStep || Expanded);
    /// <summary>Show more / less under a long text (a step opens by tapping its line instead).</summary>
    public bool ShowMore => CanExpand && !IsStep;
    public bool CanExpand => IsStep ? _detail.Length > 0 : Hidden > 0;
    public string MoreLabel => IsStep ? (Expanded ? "Hide" : "Show") : Expanded ? "Show less" : $"Show {Hidden} more line{(Hidden == 1 ? "" : "s")}";

    [RelayCommand]
    void Toggle() { if (CanExpand) Expanded = !Expanded; }
}
