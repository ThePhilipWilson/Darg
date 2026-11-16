using System.Text.Json.Nodes;
using Avalonia.Threading;
using Workbench.Core;

namespace Workbench.Desktop.ViewModels;

/// <summary>The app's commands in the Workbench API (WOR-0032): what Fettle does that isn't editing a file.
/// Files (cards, notes, docs) it changes directly, and Workbench picks them up; through these it moves the
/// app itself: shows things, starts and talks to agents, runs flows and saved commands. Every command runs
/// on the UI thread. A new one goes here, with a usage line that says what it takes: Fettle reads the list.</summary>
public partial class MainViewModel
{
    void RegisterApi(WorkbenchApi api)
    {
        void Add(string name, string usage, Func<ApiArgs, string> run) =>
            api.Add(name, usage, a => Dispatcher.UIThread.InvokeAsync(() => run(a)).GetTask());

        Add("projects", "the projects: id, name, folder, which sections each has, saved commands, and which one is on screen.", _ => Json(ProjectList()));
        Add("where", "what Philip is looking at: project, section and what's open there.", _ => Where());
        Add("show", "bring something up on screen. {project, section?, card?, doc?, agent?, artifact?}: section is one of " +
                    string.Join(", ", Enum.GetNames<Section>()) + "; card an ID (opens its panel), doc a path or name in the project's docs, " +
                    "agent an agent id, artifact a file path. With only project, switches to its tab.", Show);
        Add("agents", "every project's agents: project, id, name, state, what it's doing, open (session running), card, flow.", _ => ListAgents());
        Add("agent", "one agent: {id, last?}. Its details and transcript, the last n entries (default 20).", OneAgent);
        Add("start-agent", "start an agent in a project with a prompt: {project, prompt, cwd?, name?}. Returns its id. It costs a session: only when Philip asks.", StartAgent);
        Add("work-on", "start an agent on a board card, as the card's Work on button does (moves it to In Progress): {project, card}. Only when Philip asks.", WorkOn);
        Add("say", "send a message to a project's agent, as if Philip typed it: {id, text}. A saved agent resumes its session.", Say);
        Add("close-agents", "close agents' sessions: {ids} (a list, or ids separated by spaces). They stay listed and carry on when sent a message.", a => CloseAgents(a.List("ids")));
        Add("forget-agents", "forget agents, as the × in the Agents list does: {ids}. Each one's session ends, it leaves the list and its saved file goes " +
                             "(Claude Code keeps its history). Not one mid-turn. Only when Philip asks, or for agents whose card is done.", a => ForgetAgents(a.List("ids")));
        Add("flows", "the flows on file: name, max rounds, and the roles in order.", _ => Json(FlowList()));
        Add("run-flow", "run a flow: {project, flow, card} on a board card, or {project, flow, task} on a task in words. Only when Philip asks.", RunFlow);
        Add("run-command", "run one of a project's saved commands in its Terminal section: {project, command} (the command's name).", RunCommand);
    }

    static string Json(JsonNode n) => WorkbenchApi.Json(n);

    ProjectViewModel FindProject(ApiArgs a)
    {
        var key = a.Need("project");
        return Projects.FirstOrDefault(p => p.Config.Id.Equals(key, StringComparison.OrdinalIgnoreCase))
               ?? Projects.FirstOrDefault(p => p.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
               ?? throw new ApiError($"No project \"{key}\". Projects: {string.Join(", ", Projects.Select(p => $"{p.Config.Id} ({p.Name})"))}.");
    }

    (ProjectViewModel Project, AgentViewModel Agent) FindAgent(string id)
    {
        foreach (var p in Projects)
            if (p.Agents?.Find(id) is { } a) return (p, a);
        throw new ApiError($"No agent \"{id}\". GET agents lists them.");
    }

    JsonArray ProjectList()
    {
        var list = new JsonArray();
        foreach (var p in Projects)
        {
            var sections = new JsonArray();
            foreach (var s in Enum.GetValues<Section>()) if (p.Has(s)) sections.Add(s.ToString());
            list.Add(new JsonObject
            {
                ["id"] = p.Config.Id, ["name"] = p.Name, ["folder"] = p.Config.WorkingFolder(), ["onScreen"] = p == Selected,
                ["section"] = p.Section.ToString(), ["sections"] = sections,
                ["board"] = p.Board?.SelectedBoard, ["boardsFolder"] = p.Config.Boards is null ? null : Path.GetFullPath(p.Config.Boards),
                ["notes"] = p.Config.NotesFolder(), ["docs"] = p.Config.Docs,
                ["repos"] = new JsonArray(p.Config.Repos.Select(r => (JsonNode?)r).ToArray()),
                ["commands"] = new JsonArray(p.Config.Commands.Select(c => (JsonNode?)c.Name).ToArray()),
            });
        }
        return list;
    }

    string Show(ApiArgs a)
    {
        var p = FindProject(a);
        Setup = null; ToolsPage = null; // a page over the tabs would hide what's shown
        Selected = p;
        var done = new List<string> { $"{p.Name} on screen" };
        if (a.Get("section") is { } name)
        {
            if (!Enum.TryParse<Section>(name, true, out var s) || !p.Has(s))
                throw new ApiError($"{p.Name} has no section \"{name}\". It has: {string.Join(", ", Enum.GetValues<Section>().Where(p.Has))}.");
            p.OpenCommand.Execute(s.ToString());
            done.Add($"{s} open");
        }
        if (a.Get("card") is { } card)
        {
            if (p.Board is null) throw new ApiError($"{p.Name} has no board.");
            if (!p.Board.OpenById(card)) throw new ApiError($"No card {card} on {p.Name}'s boards.");
            p.OpenCommand.Execute(nameof(Section.Board));
            done.Add($"card {card} open");
        }
        if (a.Get("doc") is { } doc)
        {
            if (p.Docs is null) throw new ApiError($"{p.Name} has no docs.");
            var rel = Path.IsPathRooted(doc) ? Path.GetRelativePath(p.Docs.Folder, doc) : doc;
            if (rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) rel = rel[..^3];
            p.OpenCommand.Execute(nameof(Section.Docs));
            p.Docs.OpenLink("wiki:" + Path.GetFileName(rel.Replace('\\', '/')));
            done.Add(p.Docs.Selected is { } open ? $"doc {open.Path} open" : $"no doc called {doc}");
        }
        if (a.Get("agent") is { } id)
        {
            if (p.Agents?.Find(id) is not { } agent) throw new ApiError($"{p.Name} has no agent \"{id}\".");
            p.OpenCommand.Execute(nameof(Section.Agents));
            p.Agents.Selected = agent;
            done.Add($"agent \"{agent.Name}\" open");
        }
        if (a.Get("artifact") is { } file)
        {
            if (!File.Exists(file) || ArtifactStore.Owner(file) is null) throw new ApiError($"\"{file}\" isn't an artifact.");
            OpenArtifact(file);
            done.Add($"artifact {Path.GetFileName(file)} open");
        }
        return string.Join("; ", done) + ".";
    }

    string OneAgent(ApiArgs a)
    {
        var (p, agent) = FindAgent(a.Need("id"));
        var last = int.TryParse(a.Get("last"), out var n) && n > 0 ? n : 20;
        var items = new JsonArray();
        foreach (var item in agent.Items.TakeLast(last))
            items.Add(item switch
            {
                UserItem u => new JsonObject { ["kind"] = "user", ["text"] = u.Text },
                TextItem t => new JsonObject { ["kind"] = "agent", ["text"] = t.Text },
                NoteItem note => new JsonObject { ["kind"] = "note", ["text"] = note.Text },
                ToolItem tool => new JsonObject { ["kind"] = "tool", ["text"] = tool.Summary },
                _ => new JsonObject { ["kind"] = item.GetType().Name.Replace("Item", "").ToLowerInvariant() },
            });
        return Json(new JsonObject
        {
            ["project"] = p.Config.Id, ["id"] = agent.Id, ["name"] = agent.Name, ["state"] = agent.StateText, ["activity"] = agent.Activity,
            ["open"] = agent.Open, ["card"] = agent.CardId, ["cwd"] = agent.Cwd, ["flow"] = agent.Flow?.Status, ["items"] = items,
        });
    }

    string StartAgent(ApiArgs a)
    {
        var p = FindProject(a);
        if (p.Agents is null) throw new ApiError($"{p.Name} doesn't use agents.");
        var cwd = a.Get("cwd");
        if (cwd is not null && !Directory.Exists(cwd)) throw new ApiError($"No folder {cwd}.");
        var agent = p.Agents.Start(a.Need("prompt"), cwd, name: a.Get("name"));
        return $"Started \"{agent.Name}\" (id {agent.Id}) in {p.Name}.";
    }

    string WorkOn(ApiArgs a)
    {
        var p = FindProject(a);
        if (p.Board is null || p.Agents is null) throw new ApiError($"{p.Name} needs a board and agents for that.");
        var id = a.Need("card");
        if (p.Board.Show(id) is not { } card) throw new ApiError($"No card {id} on {p.Name}'s boards.");
        if (p.Agents.ForCard(id) is { } busy) throw new ApiError($"{id} already has an agent: \"{busy.Name}\" (id {busy.Id}, {busy.StateText}).");
        p.Board.StartWork(card);
        return p.Agents.ForCard(id) is { } started ? $"Started \"{started.Name}\" (id {started.Id}) on {id}." : $"Couldn't start an agent on {id}.";
    }

    string Say(ApiArgs a)
    {
        var (_, agent) = FindAgent(a.Need("id"));
        var text = a.Need("text");
        if (agent.State is AgentState.Starting or AgentState.Working)
            throw new ApiError($"\"{agent.Name}\" is mid-turn ({agent.StateText}); send when it's done.");
        agent.Say(text, text);
        return $"Sent to \"{agent.Name}\".";
    }

    string ForgetAgents(IReadOnlyList<string> ids)
    {
        var lines = new List<string>();
        foreach (var id in ids)
        {
            var hit = Projects.Select(p => (p, a: p.Agents?.Find(id))).FirstOrDefault(x => x.a is not null);
            if (hit.a is not { } agent) { lines.Add($"{id}: no such agent"); continue; }
            if (agent.State is AgentState.Starting or AgentState.Working) { lines.Add($"{id} ({agent.Name}): mid-turn, left alone"); continue; }
            hit.p.Agents!.ForgetCommand.Execute(agent);
            lines.Add($"{id} ({agent.Name}): forgotten");
        }
        return string.Join(Environment.NewLine, lines);
    }

    JsonArray FlowList()
    {
        var list = new JsonArray();
        foreach (var f in FlowStore.List())
            list.Add(new JsonObject
            {
                ["name"] = f.Name, ["maxRounds"] = f.MaxRounds,
                ["roles"] = new JsonArray(f.Stages.Select(s => (JsonNode?)s.Role).ToArray()),
            });
        return list;
    }

    string RunFlow(ApiArgs a)
    {
        var p = FindProject(a);
        if (p.Agents is null) throw new ApiError($"{p.Name} doesn't use agents.");
        p.Agents.ReloadFlows();
        var name = a.Need("flow");
        var flow = p.Agents.Flows.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                   ?? throw new ApiError($"No flow \"{name}\". Flows: {string.Join(", ", p.Agents.Flows.Select(f => f.Name))}.");
        if (a.Get("card") is { } id)
        {
            if (p.Board?.Show(id) is not { } card) throw new ApiError($"No card {id} on {p.Name}'s boards.");
            p.Board.StartFlow(card, flow);
            return $"Running {flow.Name} on {id}.";
        }
        var task = a.Get("task") ?? throw new ApiError("Give a card or a task.");
        p.Agents.RunFlow(flow, task, ChatTitle.Guess(task));
        return $"Running {flow.Name} on the task.";
    }

    string RunCommand(ApiArgs a)
    {
        var p = FindProject(a);
        if (p.Consoles is null) throw new ApiError($"{p.Name} has no terminal.");
        var name = a.Need("command");
        var c = p.Config.Commands.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new ApiError($"No saved command \"{name}\". {p.Name} has: {string.Join(", ", p.Config.Commands.Select(x => x.Name))}.");
        p.OpenCommand.Execute(nameof(Section.Terminal));
        p.Consoles.Run(c);
        return $"Running {c.Name} in {p.Name}'s terminal.";
    }
}
