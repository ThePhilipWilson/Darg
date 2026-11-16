using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>A flow under way: one agent per stage, started in turn. When a stage's turn ends, its last reply
/// goes on to the next stage. A checking stage's FAIL sends its report back to the stage it names, which
/// fixes and hands on again, up to the flow's max rounds. Runs on its own; stops on an error, a missing
/// verdict, rounds running out, or Stop flow.</summary>
public sealed partial class FlowRun : ObservableObject
{
    readonly Flow _flow;
    readonly string _task;
    readonly Func<FlowRun, int, string, AgentViewModel> _start;
    readonly AgentViewModel?[] _agents;
    /// <summary>The latest report from each stage, by stage index.</summary>
    readonly string?[] _reports;
    AgentViewModel? _waiting;
    int _stage;
    int _round;

    public string Name => _flow.Name;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Running))] public partial string Status { get; set; } = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Running))] public partial bool Ended { get; set; }
    public bool Running => !Ended;
    /// <summary>The flow is over: (the last agent, a line on how it ended, whether it passed).</summary>
    public event Action<AgentViewModel?, string, bool>? Finished;

    /// <param name="start">Starts a stage's agent: (this run, stage index, first message).</param>
    public FlowRun(Flow flow, string task, Func<FlowRun, int, string, AgentViewModel> start)
    {
        _flow = flow; _task = task; _start = start;
        _agents = new AgentViewModel?[flow.Stages.Count];
        _reports = new string?[flow.Stages.Count];
    }

    public void Begin() => Run(0);

    string Where => $"{_flow.Stages[_stage].Role} · stage {_stage + 1} of {_flow.Stages.Count}" + (_round > 0 ? $" · round {_round + 1}" : "");

    /// <summary>Start a stage, or if it already has an agent (after a send-back), tell it what changed.</summary>
    void Run(int stage, string? sentBy = null)
    {
        _stage = stage;
        var s = _flow.Stages[stage];
        if (_agents[stage] is { } again)
        {
            Status = $"{_flow.Name}: {Where}";
            var from = _flow.Stages[stage - 1].Role;
            var report = _reports[stage - 1] ?? "";
            var msg = s.Checks ? FlowStore.RecheckPrompt(from, report)
                : $"The {from} has made changes after a send-back. Carry on with your brief in the light of them, and end with your hand-off as before.\n\n## The {from}'s report\n{report.Trim()}";
            Wait(again);
            again.Say($"From the {from} (round {_round + 1})", msg);
            return;
        }
        var handoffs = Enumerable.Range(0, stage).Where(i => _reports[i] is not null)
            .Select(i => (_flow.Stages[i].Role, _reports[i]!)).ToList();
        var a = _start(this, stage, FlowStore.StagePrompt(_flow, stage, _task, handoffs));
        _agents[stage] = a;
        a.TurnEnded += error => OnTurn(a, error);
        Wait(a);
        Status = $"{_flow.Name}: {Where}"; // after the agent is listed, so whatever shows the status sees it
    }

    void Wait(AgentViewModel a) => _waiting = a;

    void OnTurn(AgentViewModel a, bool error)
    {
        if (Ended || a != _waiting) return; // a turn the flow didn't ask for (the person talking to it after)
        _waiting = null;
        if (error) { End(a, $"stopped: the {_flow.Stages[_stage].Role} ended with an error", false); return; }
        var report = a.LastReply();
        _reports[_stage] = report;
        var s = _flow.Stages[_stage];
        if (FlowStore.AsksToStop(report)) { End(a, $"the {s.Role} said it can't succeed (FLOW: STOP)", false); return; }
        if (s.Checks)
        {
            switch (FlowStore.VerdictOf(report))
            {
                case Verdict.None:
                    End(a, $"stopped: the {s.Role} gave no VERDICT line", false);
                    return;
                case Verdict.Fail when _round >= _flow.MaxRounds:
                    End(a, $"{s.Role} still says FAIL after {_flow.MaxRounds} round(s) of fixes", false);
                    return;
                case Verdict.Fail:
                    _round++;
                    var back = FlowStore.IndexOf(_flow, s.SendsBackTo!);
                    a.AddNote($"Sent back to the {_flow.Stages[back].Role} (round {_round} of {_flow.MaxRounds})");
                    _stage = back;
                    Status = $"{_flow.Name}: {Where}";
                    var fixer = _agents[back]!;
                    Wait(fixer);
                    fixer.Say($"Sent back by the {s.Role} (round {_round} of {_flow.MaxRounds})",
                        FlowStore.SendBackPrompt(s.Role, report, _round, _flow.MaxRounds));
                    return;
            }
        }
        if (_stage + 1 < _flow.Stages.Count)
        {
            a.AddNote($"Handed on to the {_flow.Stages[_stage + 1].Role}");
            Run(_stage + 1);
        }
        else End(a, s.Checks ? $"{s.Role} says PASS" + (_round > 0 ? $" after {_round} round(s) of fixes" : "") : "done", true);
    }

    void End(AgentViewModel? a, string how, bool ok)
    {
        Ended = true;
        _waiting = null;
        Status = $"{_flow.Name}: {how}";
        a?.AddNote($"Flow {(ok ? "finished" : "ended")}: {how}", !ok);
        Finished?.Invoke(a, how, ok);
    }

    /// <summary>Stop handing on. The agent working now carries on with its turn; nothing starts after it.</summary>
    [RelayCommand]
    void StopFlow()
    {
        if (Ended) return;
        End(_waiting, "stopped by you", false);
    }
}
