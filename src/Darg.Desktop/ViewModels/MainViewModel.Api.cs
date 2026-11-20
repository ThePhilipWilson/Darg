using System.Text.Json.Nodes;
using Avalonia.Threading;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>The app's commands in the Darg API (WOR-0032): what Fettle does that isn't editing a file.
/// Files (cards, notes, docs) it changes directly, and Darg picks them up; through these it moves the
/// app itself: shows things, starts and talks to agents, runs flows and saved commands. Every command runs
/// on the UI thread. A new one goes here, with a usage line that says what it takes: Fettle reads the list.</summary>
public partial class MainViewModel
{
    void RegisterApi(DargApi api)
    {
        void Add(string name, string usage, Func<ApiArgs, string> run) =>
            api.Add(name, usage, a => Dispatcher.UIThread.InvokeAsync(() => run(a)).GetTask());

        Add("projects", "the projects: id, name, folder, which sections each has, saved commands, and which one is on screen.", _ => Json(ProjectList()));
        Add("where", "what Philip is looking at: project, section and what's open there.", _ => Where());
        api.Add("overview", "what's going on in a project, in one call: {project}. Its Home page as JSON: the shown board's card count per column, " +
                            "cards in its middle columns with the agent on each, agents needing Philip or at work, the newest artifacts, unread reports, " +
                            "and per repo its branch, uncommitted file count and last commits.", Overview);
        // read-only views for the phone (AND-0008): files are read off the UI thread, only the project lookup is on it
        void Read(string name, string usage, Func<ApiArgs, ProjectConfig, string> run) =>
            api.Add(name, usage, async a =>
            {
                var config = await Dispatcher.UIThread.InvokeAsync(() => FindProject(a).Config);
                return await Task.Run(() => run(a, config));
            });
        Read("notes", "a project's notes wall, read only: {project}. Its columns and every note (id, column, colour, tags, created, ticket, text).",
            (_, c) => Json(ReadViews.NotesList(c.NotesFolder())));
        Read("docs", "the docs in a project's docs folder, read only: {project}. Paths relative to it, to give to doc.",
            (_, c) => Json(new JsonArray((c.Docs is null ? [] : ReadViews.DocList(c.Docs)).Select(d => (JsonNode?)d).ToArray())));
        Read("doc", "one doc's markdown, read only: {project, path} (a path from docs, or a name without .md). Returns path, title, text and links: " +
                    "for each link in the text (wiki:Name for [[Name]], else the URL) where it goes: doc (ask for that path next), external, elsewhere or missing.",
            (a, c) => Json(ReadViews.Doc(c.Docs ?? throw new ApiError("That project has no docs folder."), a.Need("path"))));
        Read("reports", "a project's finished-agent reports, newest first, read only: {project, unread?: true}. Each: id, card, title, kind (finished, investigation, qa), date, read, commits.",
            (a, c) => Json(ReadViews.ReportList(ReportStore.FolderFor(c.Id), a.Get("unread") == "true")));
        Read("report", "one report, read only: {project, id} (from reports). Its details and the markdown body.",
            (a, c) => Json(ReadViews.Report(ReportStore.FolderFor(c.Id), a.Need("id"))));
        Add("show", "bring something up on screen. {project, section?, card?, doc?, agent?, artifact?}: section is one of " +
                    string.Join(", ", Enum.GetNames<Section>()) + "; card an ID (opens its panel), doc a path or name in the project's docs, " +
                    "agent an agent id, artifact a file path. With only project, switches to its tab.", Show);
        Add("agents", "a project's agents: {project}, or {} for every project's (only when Philip asks about all of them). Each: project, id, name, state, " +
                      "what it's doing, open (session running), card, flow.", a => ListAgents(a.Get("project") is null ? null : FindProject(a)));
        Add("cards", CardsUsage, Cards);
        Add("card", CardUsage, OneCard);
        Add("agent", "one agent: {id, last?}. Its details and transcript, the last n entries (default 20).", OneAgent);
        Add("start-agent", "start an agent in a project with a prompt: {project, prompt, cwd?, name?}. Returns its id. It costs a session: only when Philip asks.", StartAgent);
        Add("work-on", "start an agent on a board card, as the card's Work on button does (moves it to In Progress): {project, card}, or on several at once as ▶ Work on N does: {project, cards} (a list, or ids separated by spaces). A card blocked by one not yet in QA or done is refused (left out of several) unless force: true. Only when Philip asks.", WorkOn);
        Add("work-all", "start an agent on every card in the shown board's backlog and In Progress with none on it, as that column's ▶ Work All does (backlog cards move to In Progress; no asking first): {project}. Cards blocked by one not yet in QA or done are left out and named unless force: true. Only when Philip asks.", a => BulkApi(a, BulkStart.Kind.Work));
        Add("qa-all", "run QA on every card in the shown board's QA column with no QA agent on it, as its 🧪 QA Run All does (no asking first): {project}. Only when Philip asks.", a => BulkApi(a, BulkStart.Kind.Qa));
        Add("improve-card", "start an agent that fills a thin card out (what's wanted, where in the code, done when), asking Philip first if its words are unclear; the card's Improve button: {project, card}, or an agent each on several at once as ✦ Improve N does: {project, cards} (a list, or ids separated by spaces). Only when Philip asks.", ImproveCard);
        Add("send-back", "send a card in the QA column back with what's wrong, as its panel's Send back does: {project, card, notes}. The notes go into the card " +
                         "under QA notes, it returns to In Progress and an agent reworks it from them. Only when Philip asks, with his words as the notes.", SendBack);
        Add("decide", "answer the decision a card in For Human Attention waits on, as picking an option on its panel does: {project, card, answer, notes?}. " +
                      "answer is an option's label (its action runs: done, send back or stay) or Philip's own words (they go to the agent that asked, or a new one). " +
                      "A card with no decision answers Approve or Send back. Only with Philip's answer.", Decide);
        Add("to-attention", "move a card to For Human Attention with the decision Philip has to make: {project, card, question, options, context?}. " +
                            "options is a list of 2 to 4 lines, each Label (recommended) → done|back|stay: what it means (one marked recommended; no arrow sends his answer to an agent). " +
                            "Refused without a question and two options.", ToAttention);
        Add("new-card", "make a card in a project's backlog, on its inbox board (Inbox, Main or Backlog), as + Card does: {project, title, description?, " +
                        "type?: " + string.Join("|", CardTypes.All) + " (else guessed from the words), labels? (a list, or separated by commas), priority?}. " +
                        "Returns its id, board, file and link (markdown that opens the card when clicked: put it in your reply); Haiku then moves it to the story board it fits, if the project has others. Use this rather than writing card files. " +
                        "board? and column? (a name, or backlog, qa, attention, rework, done) put it straight there instead, with no placing (the phone's quick-add).", NewCard);
        Add("boards", "a project's boards column by column, empty columns too, as the phone draws them: {project}. Each board: name, inbox (where new-card puts a card), " +
                      "lists in order, each with id, name, kind (" + string.Join(", ", BoardLayouts.Kinds) + ", or empty for a plain column) and its cards " +
                      "(id, title, type, labels, priority, quality 0-3, agent: the live one's id and state, or null, " +
                      "blockedBy: the cards it waits on with found, cleared and cycle, blocked: any still open, blocks: the cards waiting on it).", BoardsLayout);
        Add("move-card", "move a card to another column of its board, as dragging it on the board does: {project, card, column, before?}. column is a column's name, " +
                         "or backlog, qa, attention, rework, done or completed; before a card in that column to land above (else the end). Only when Philip asks.", MoveCard);
        Add("say","send a message to a project's agent, as if Philip typed it: {id, text}. A saved agent resumes its session.", Say);
        Add("close-agents", "close agents' sessions: {ids} (a list, or ids separated by spaces). They stay listed and carry on when sent a message.", a => CloseAgents(a.List("ids")));
        Add("forget-agents", "forget agents, as the × in the Agents list does: {ids}. Each one's session ends, it leaves the list and its saved file goes " +
                             "(Claude Code keeps its history). Not one mid-turn. Only when Philip asks, or for agents whose card is done.", a => ForgetAgents(a.List("ids")));
        Add("flows", "the flows on file: name, max rounds, the roles in order, and shared or the project's own. {project?}: that project's as well as the shared ones.",
            a => Json(FlowList(a.Get("project") is null ? null : FindProject(a))));
        Add("new-flow", "start an agent that writes a new flow (the Agents view's + by Run flow): {project, description, kind?: basic|art, shared?: true for all projects, " +
                        "else the project's own}. Returns its id. Only when Philip asks.", NewFlow);
        Add("run-flow", "run a flow: {project, flow, card} on a board card, or {project, flow, task} on a task in words. Only when Philip asks.", RunFlow);
        Add("theme", "the app's colours: {theme} switches to that theme (" + string.Join(", ", Themes.All.Select(t => t.Id)) + ") and saves it; " +
                     "{} says which is on. Only when Philip asks.", SetThemeApi);
        Add("optimise-tokens", "the header's Optimise tokens toggle: {on: true|false} turns it on (every agent one model down and at no more than " +
                               "medium effort, running ones from their next turn) or off; {} says whether it's on. Only when Philip asks.", OptimiseTokensApi);
        Add("push", "phone notifications through ntfy (AND-0006): {} says how they stand; {topic: new} turns them on with a fresh secret topic " +
                    "(or {topic, server?, token?} for a given one), {off: true} turns them off. Philip subscribes to the topic in the ntfy app; a card waiting on a decision " +
                    "shows its options as buttons there. Only when Philip asks.", PushApi);
        api.Add("push-test", "send a test notification to the phone, to check it arrives: {}.", _ => Dispatcher.UIThread.InvokeAsync(PushTest));
        Add("run-command", "run one of a project's saved commands in its Terminal section: {project, command} (the command's name).", RunCommand);
        // AND-0007: Fettle from the phone. The phone app calls these; Fettle itself has no use for them.
        Add("fettle-say", "for the phone app, not Fettle: send Philip's words to Fettle as if typed in its box, with what the phone shows: {project, text, chat?, section?, card?, doc?, voice?}. " +
                          "No chat: a new one. voice: true when dictated. Returns the chat as fettle does, from his message.", FettleSay);
        Add("fettle", "for the phone app, not Fettle: a Fettle chat: {chat, from?}. Its state and its transcript from entry from (default 0): user, fettle, note, tool, " +
                      "question (with its questions and options to tap) and permission entries, each with its index.", a => FettleRead(a.Need("chat"), FromArg(a)));
        Add("fettle-answer", "for the phone app, not Fettle: answer what a Fettle chat waits on: {chat, answers} (question text to the label picked, or his own words) " +
                             "for a question, {chat, allow: true|false} for a tool it wants to use.", FettleAnswer);
        Add("fettle-chats", "for the phone app, not Fettle: a project's Fettle chats, newest first: {project}. Each: id, name, updated.", FettleChats);
        RegisterPhoneApi(api);
    }

    static string Json(JsonNode n) => DargApi.Json(n);

    string SetThemeApi(ApiArgs a)
    {
        var names = string.Join(", ", Themes.All.Select(t => t.Id));
        if (a.Get("theme") is not { } name) return $"{ThemeManager.Current.Name} is on. Themes: {names}.";
        var theme = Themes.Find(name) ?? throw new ApiError($"No theme \"{name}\". Themes: {names}.");
        var why = SetTheme(theme);
        SettingsPage?.Refresh();
        return why ?? $"{theme.Name} is on.";
    }

    string OptimiseTokensApi(ApiArgs a)
    {
        if (a.Get("on") is not { } text) return $"Optimise tokens is {(TokenSaver.On ? "on" : "off")}.";
        var on = text.Trim().ToLowerInvariant() switch
        {
            "true" or "on" or "yes" or "1" => true,
            "false" or "off" or "no" or "0" => false,
            _ => throw new ApiError($"on takes true or false, not \"{text}\"."),
        };
        var why = SetOptimiseTokens(on);
        return $"Optimise tokens is {(on ? "on" : "off")}." + (why is null ? "" : $" Couldn't save it: {why}");
    }

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

    /// <summary>The overview command (WOR-0141): what Home shows, as JSON. The board and agents are read on the UI
    /// thread, where they live; files and git (reports, artifacts, status, log) off it, as Home's own Load does.</summary>
    async Task<string> Overview(ApiArgs a)
    {
        var (result, config, artifacts, reports) = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var p = FindProject(a);
            var o = new JsonObject { ["project"] = p.Config.Id, ["name"] = p.Name };
            if (p.Board is { } board)
            {
                o["board"] = board.SelectedBoard;
                var columns = new JsonObject();
                foreach (var (name, count) in board.ColumnCounts()) columns[name] = count;
                o["columns"] = columns;
                var cards = new JsonArray();
                foreach (var (column, card) in board.InProgress())
                    cards.Add(new JsonObject
                    {
                        ["id"] = card.Id, ["title"] = card.Title, ["column"] = column,
                        ["agent"] = p.Agents?.ForCard(card.Id) is { } on
                            ? new JsonObject { ["id"] = on.Id, ["name"] = on.Name, ["state"] = on.State.ToString().ToLowerInvariant() }
                            : null,
                    });
                o["inProgress"] = cards;
            }
            if (p.Agents is { } agents)
            {
                // as Home lists them: the ones waiting on Philip first, then the ones at work
                var live = new JsonArray();
                foreach (var ag in agents.Agents.Where(x => x.NeedsYou || x.Working).OrderBy(x => x.NeedsYou ? 0 : 1))
                    live.Add(new JsonObject
                    {
                        ["id"] = ag.Id, ["name"] = ag.Name, ["state"] = ag.State.ToString().ToLowerInvariant(), ["needsYou"] = ag.NeedsYou,
                        ["doing"] = ag.Activity, ["card"] = ag.CardId,
                    });
                o["agents"] = live;
                o["otherAgents"] = agents.Agents.Count - live.Count;
            }
            return (o, p.Config, p.Artifacts is not null, p.Reports?.Folder);
        });
        return await Task.Run(() =>
        {
            if (artifacts)
                result["artifacts"] = new JsonArray(ArtifactStore.List(config.Id).Take(5).Select(x => (JsonNode?)new JsonObject
                {
                    ["title"] = x.Title, ["path"] = x.Path, ["agent"] = x.Agent, ["modified"] = x.Modified.ToString("yyyy-MM-dd HH:mm"),
                }).ToArray());
            if (reports is not null)
                result["unreadReports"] = new JsonArray(ReportStore.List(reports).Where(r => !r.Read).Select(r => (JsonNode?)new JsonObject
                {
                    ["card"] = r.Card, ["title"] = r.Title, ["kind"] = r.Kind.ToString().ToLowerInvariant(), ["date"] = r.Date, ["file"] = r.File,
                }).ToArray());
            var repos = new JsonArray();
            foreach (var root in config.ExistingRepos().Where(Repo.IsGit))
            {
                var status = Repo.Status(root);
                repos.Add(new JsonObject
                {
                    ["name"] = ProjectConfig.RepoName(root), ["path"] = root, ["branch"] = status.Branch,
                    ["uncommitted"] = status.Changed.Count, ["ahead"] = status.Ahead, ["behind"] = status.Behind,
                    ["commits"] = new JsonArray(Repo.Log(root, 5).Select(c => (JsonNode?)new JsonObject
                    {
                        ["hash"] = c.Hash, ["subject"] = c.Subject, ["author"] = c.Author, ["when"] = c.When.ToString("yyyy-MM-dd HH:mm"),
                    }).ToArray()),
                });
            }
            result["repos"] = repos;
            return Json(result);
        });
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
            if (Selected != p && Selected?.Docs?.Selected is { } there) done.Add($"doc {there.Path} open in {Selected.Name}"); // another project's docs held it
            else done.Add(p.Docs.Selected is { } open ? $"doc {open.Path} open" : $"no doc called {doc}");
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
        var force = Force(a);
        if (a.Get("cards") is not null) return p.Board.WorkOnCards(a.List("cards"), force);
        var id = a.Need("card");
        if (p.Board.Show(id) is not { } card) throw new ApiError($"No card {id} on {p.Name}'s boards.");
        if (p.Agents.ForCard(id) is { } busy) throw new ApiError($"{id} already has an agent: \"{busy.Name}\" (id {busy.Id}, {busy.StateText}).");
        // WOR-0173: a card waiting on another not yet in QA or done isn't started unless told to
        if (!force && !p.Board.InQa(card.Card) && p.Board.OpenBlockers(card.Card, fresh: true) is { Count: > 0 } open)
            throw new ApiError(BlockedStart.Refusal(card.Id, open));
        p.Board.StartWork(card, force: true);
        return p.Agents.ForCard(id) is { } started ? $"Started \"{started.Name}\" (id {started.Id}) on {id}." : $"Couldn't start an agent on {id}.";
    }

    string BulkApi(ApiArgs a, BulkStart.Kind kind)
    {
        var p = FindProject(a);
        if (p.Board is null || p.Agents is null) throw new ApiError($"{p.Name} needs a board and agents for that.");
        return p.Board.StartBulk(kind, Force(a));
    }

    static bool Force(ApiArgs a) => a.Get("force") is { } f && (f.Equals("true", StringComparison.OrdinalIgnoreCase) || f is "1" or "yes");

    string ImproveCard(ApiArgs a)
    {
        var p = FindProject(a);
        if (p.Board is null || p.Agents is null) throw new ApiError($"{p.Name} needs a board and agents for that.");
        if (a.Get("cards") is not null) return p.Board.ImproveCards(a.List("cards"));
        var id = a.Need("card");
        if (p.Board.Show(id) is not { } card) throw new ApiError($"No card {id} on {p.Name}'s boards.");
        p.Board.StartImprove(card);
        return $"Started an agent improving {id}.";
    }

    string SendBack(ApiArgs a)
    {
        var p = FindProject(a);
        if (p.Board is null || p.Agents is null) throw new ApiError($"{p.Name} needs a board and agents for that.");
        var id = a.Need("card");
        if (p.Board.Show(id) is not { } card) throw new ApiError($"No card {id} on {p.Name}'s boards.");
        if (!p.Board.InQa(card.Card) && !p.Board.InAttention(card.Card)) throw new ApiError($"{id} isn't in the QA or needs-attention column ({card.Card.Status}); only a card there is sent back.");
        if (p.Board.SendBack(card, a.Need("notes")) is { } error) throw new ApiError(error);
        return p.Agents.ForCard(id) is { } started ? $"Sent {id} back; \"{started.Name}\" (id {started.Id}) is on it." : $"Sent {id} back.";
    }

    string Decide(ApiArgs a)
    {
        var p = FindProject(a);
        if (p.Board is null) throw new ApiError($"{p.Name} has no board.");
        var id = a.Need("card");
        if (p.Board.Show(id) is null) throw new ApiError($"No card {id} on {p.Name}'s boards.");
        if (p.Board.Decide(id, a.Need("answer"), a.Get("notes") ?? "") is { } error) throw new ApiError(error);
        return $"Answered {id}: {a.Need("answer")}.";
    }

    string ToAttention(ApiArgs a)
    {
        var p = FindProject(a);
        if (p.Board is null) throw new ApiError($"{p.Name} has no board.");
        var id = a.Need("card");
        if (p.Board.Show(id) is not { } card) throw new ApiError($"No card {id} on {p.Name}'s boards.");
        // each option as the card writes it: a JSON list of lines, or lines in one string
        var raw = a.Get("options") ?? throw new ApiError("Missing \"options\".");
        IEnumerable<string> lines;
        try { lines = JsonNode.Parse(raw) is JsonArray list ? list.Select(x => x?.ToString() ?? "") : raw.Split('\n'); }
        catch (System.Text.Json.JsonException) { lines = raw.Split('\n'); }
        var text = $"Question: {a.Need("question")}\n" + string.Join("\n", lines.Select(l => l.Trim()).Where(l => l.Length > 0).Select(l => l.StartsWith('-') ? l : "- " + l));
        var decision = CardDecision.Parse(text);
        if (!decision.Valid || decision.Options.Count > 4) throw new ApiError("A decision needs a question and two to four options, each a line like: Label (recommended) → done: what it means.");
        decision = decision with { Context = a.Get("context") ?? "", AskedBy = $"Fettle, {DateTime.Now:yyyy-MM-dd HH:mm}" };
        if (p.Board.Folder is null) throw new ApiError($"{p.Name}'s board isn't loaded.");
        try { decision.WriteTo(Path.GetFullPath(Path.Combine(card.Board.Folder!, "items", card.Card.File))); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new ApiError($"Couldn't write the decision into {id}: {e.Message}"); }
        if (card.Board.MoveToAttention(id) is { } error) throw new ApiError(error);
        return $"Moved {id} to For Human Attention, asking: {decision.Question}";
    }

    /// <summary>A card made in one call (WOR-0139): Fettle wrote card files and board.json by hand, 4 to 6 calls a card,
    /// and its cards skipped the story-board placement a note's ticket gets.</summary>
    string NewCard(ApiArgs a)
    {
        var p = FindProject(a);
        if (p.Board?.InboxFolder is not { } inbox) throw new ApiError($"{p.Name} has no board.");
        var title = a.Need("title");
        var description = a.Get("description") ?? "";
        // labels as a list, or a string split on commas only: a label can have spaces in it
        IReadOnlyList<string> labels = [];
        if (a.Get("labels") is { } raw)
        {
            bool isList;
            try { isList = JsonNode.Parse(raw) is JsonArray; }
            catch (System.Text.Json.JsonException) { isList = false; }
            labels = isList ? a.List("labels") : raw.Split([',', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        }
        var type = a.Get("type") is { Length: > 0 } t
            ? CardTypes.All.FirstOrDefault(x => x.Equals(t.Trim(), StringComparison.OrdinalIgnoreCase))
              ?? throw new ApiError($"No card type \"{t}\". Types: {string.Join(", ", CardTypes.All)}.")
            : CardTypes.Guess(title + "\n" + description, labels);
        if ((a.Get("board") ?? a.Get("column")) is not null)
        {
            // the phone's quick-add (AND-0004): it knows the board and column it's showing, so the card goes there, unplaced
            var boardName = a.Get("board") ?? Path.GetFileName(inbox.TrimEnd('\\', '/'));
            Card added;
            try { added = BoardLayouts.Add(p.Config.Boards!, boardName, a.Get("column"), title, description, labels, (a.Get("priority") ?? "").Trim(), type); }
            catch (ArgumentException e) { throw new ApiError(e.Message); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new ApiError($"Couldn't make the card: {e.Message}"); }
            var at = CardIndex.Find(p.Config.Boards, added.Id)!;
            p.Board.ReloadAll();
            return Json(new JsonObject
            {
                ["id"] = added.Id, ["title"] = added.Title, ["type"] = added.Type, ["board"] = at.Board, ["column"] = at.Column,
                ["file"] = at.File, ["link"] = CardLinks.For(added.Id), ["placing"] = false,
            });
        }
        Card card;
        try { card = BoardStore.Create(inbox, title, description, labels, (a.Get("priority") ?? "").Trim(), type); }
        catch (ArgumentException e) { throw new ApiError(e.Message); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new ApiError($"Couldn't make the card: {e.Message}"); }
        p.Board.PlaceLater(inbox, card.Id);
        p.Board.ReloadAll();
        return Json(new JsonObject
        {
            ["id"] = card.Id, ["title"] = card.Title, ["type"] = card.Type, ["board"] = Path.GetFileName(inbox.TrimEnd('\\', '/')),
            ["file"] = Path.GetFullPath(Path.Combine(inbox, "items", card.File)),
            ["link"] = CardLinks.For(card.Id), // WOR-0158: Fettle's reply links the card it made
            ["placing"] = p.Board.Boards.Count > 1,
        });
    }

    /// <summary>The project's boards column by column for the phone (AND-0004): <c>cards</c> leaves out empty columns and
    /// the order of the lists, which a board drawn on a phone needs.</summary>
    string BoardsLayout(ApiArgs a)
    {
        var p = FindProject(a);
        if (p.Config.Boards is null) throw new ApiError($"{p.Name} has no boards.");
        var boards = new JsonArray();
        var all = CardIndex.Read(p.Config.Boards);
        var index = Blockers.Index(all);
        foreach (var b in BoardLayouts.Read(p.Config.Boards))
            boards.Add(new JsonObject
            {
                ["name"] = b.Name, ["inbox"] = b.Inbox,
                ["lists"] = new JsonArray(b.Lists.Select(l => (JsonNode?)new JsonObject
                {
                    ["id"] = l.Id, ["name"] = l.Name, ["kind"] = l.Kind,
                    ["cards"] = new JsonArray(l.Cards.Select(c =>
                    {
                        var live = AgentsOn(p, c.Id).FirstOrDefault(x => x.IsLiveOnCard);
                        var row = new JsonObject
                        {
                            ["id"] = c.Id, ["title"] = c.Title, ["type"] = c.Kind, ["priority"] = c.Priority,
                            ["labels"] = new JsonArray(c.Labels.Select(x => (JsonNode?)x).ToArray()), ["quality"] = c.Quality.Stars,
                            ["agent"] = live is null ? null : new JsonObject { ["id"] = live.Id, ["state"] = live.StateText },
                        };
                        AddBlockers(row, c, index, all);
                        return (JsonNode?)row;
                    }).ToArray()),
                }).ToArray()),
            });
        return Json(boards);
    }

    string MoveCard(ApiArgs a)
    {
        var p = FindProject(a);
        if (p.Config.Boards is null || p.Board is null) throw new ApiError($"{p.Name} has no boards.");
        var id = a.Need("card");
        (string Board, string List) to;
        try { to = BoardLayouts.Move(p.Config.Boards, id, a.Need("column"), a.Get("before")); }
        catch (ArgumentException e) { throw new ApiError(e.Message); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new ApiError($"Couldn't move {id}: {e.Message}"); }
        p.Board.ReloadAll(); // the board on screen shows it now, not when the watcher's debounce runs
        // JSON, as the phone's client reads every reply (AND-0003's DargClient refuses one that isn't)
        return Json(new JsonObject { ["id"] = id.Trim().ToUpperInvariant(), ["board"] = to.Board, ["column"] = to.List, ["moved"] = $"Moved {id.Trim().ToUpperInvariant()} to {to.List} on {to.Board}." });
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

    static int FromArg(ApiArgs a) => int.TryParse(a.Get("from"), out var n) && n > 0 ? n : 0;

    string FettleSay(ApiArgs a)
    {
        var p = FindProject(a);
        var text = a.Need("text");
        var voice = a.Get("voice") is "true" or "1";
        var section = a.Get("section");
        if (section is not null && !Enum.TryParse<Section>(section, true, out _))
            throw new ApiError($"No section \"{section}\"; one of {string.Join(", ", Enum.GetNames<Section>())}.");
        var context = PhoneFettle.Context(p.Name, section, a.Get("card"), a.Get("doc"));
        var chat = Mascot.PhoneSay(p.Config.Id, a.Get("chat"), text, PhoneFettle.Message(context, text, voice));
        // from his message on: the phone shows it at once, and Fettle's reply follows as it polls
        var from = chat.Items.Select((item, i) => (item, i)).LastOrDefault(x => x.item is UserItem).i;
        return FettleRead(chat, from);
    }

    string FettleRead(string chatId, int from) =>
        FettleRead(Mascot.Find(chatId) ?? throw new ApiError($"No Fettle chat \"{chatId}\"."), from);

    static string FettleRead(AgentViewModel chat, int from)
    {
        var items = new JsonArray();
        var all = chat.Items.ToList();
        for (var i = Math.Min(from, all.Count); i < all.Count; i++) items.Add(FettleEntryFor(all[i], i).ToJson());
        return Json(new JsonObject
        {
            ["id"] = chat.Id, ["name"] = chat.Name, ["project"] = chat.Project, ["state"] = chat.StateText,
            ["busy"] = chat.Working, ["needsYou"] = chat.NeedsYou, ["count"] = all.Count, ["from"] = from, ["items"] = items,
        });
    }

    static FettleEntry FettleEntryFor(TranscriptItem item, int i) => item switch
    {
        UserItem u => new(i, "user", u.Text, null, false, []),
        TextItem t => new(i, "fettle", t.Text, null, false, []),
        NoteItem n => new(i, "note", n.Text, null, false, []),
        ToolItem t => new(i, "tool", t.Name, t.Summary, false, []),
        QuestionItem q => new(i, "question", string.Join(" / ", q.Questions.Select(g => g.Question)), null, q.Pending,
            q.Questions.Select(g => new FettleQuestion(g.Header, g.Question, g.MultiSelect, g.Options.Select(o => new FettleOption(o.Label, o.Description)).ToList(), g.Chosen)).ToList()),
        PermissionItem p => new(i, "permission", p.Tool, p.Summary, p.Pending, []),
        _ => new(i, item.GetType().Name.Replace("Item", "").ToLowerInvariant(), "", null, false, []),
    };

    string FettleAnswer(ApiArgs a)
    {
        var chat = Mascot.Find(a.Need("chat")) ?? throw new ApiError($"No Fettle chat \"{a.Get("chat")}\".");
        var waiting = chat.Items.OfType<IPending>().LastOrDefault(x => x.Pending) as TranscriptItem
                      ?? throw new ApiError($"Fettle isn't waiting on anything in \"{chat.Name}\".");
        var from = chat.Items.IndexOf(waiting);
        switch (waiting)
        {
            case QuestionItem q:
                var given = a.Get("answers") is { } json ? JsonNode.Parse(json) as JsonObject : null;
                if (given is null || given.Count == 0) throw new ApiError("Missing \"answers\": each question's text to the label picked.");
                foreach (var g in q.Questions)
                {
                    var answer = (string?)given[g.Question]
                                 ?? (q.Questions.Count == 1 && given.Count == 1 ? (string?)given.First().Value : null) // one question: its text needn't match exactly
                                 ?? throw new ApiError($"No answer for \"{g.Question}\".");
                    q.Pick(g.Question, answer);
                }
                break;
            case PermissionItem p:
                var allow = a.Need("allow") is "true" or "1" or "yes";
                (allow ? p.AllowCommand : p.DenyCommand).Execute(null);
                break;
        }
        return FettleRead(chat, from);
    }

    string FettleChats(ApiArgs a)
    {
        var p = FindProject(a);
        var list = new JsonArray();
        foreach (var r in AgentStore.List(AgentStore.Folder("~bench"))
                     .Where(r => r.Project is null || string.Equals(r.Project, p.Config.Id, StringComparison.OrdinalIgnoreCase)))
            list.Add(new JsonObject { ["id"] = r.Id, ["name"] = r.Name, ["updated"] = r.Updated.ToString("s") });
        return Json(list);
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

    JsonArray FlowList(ProjectViewModel? p)
    {
        var list = new JsonArray();
        foreach (var f in p is null ? FlowStore.List() : FlowStore.ForProject(p.Config.Id.Length > 0 ? p.Config.Id : "default"))
            list.Add(new JsonObject
            {
                ["name"] = f.Name, ["maxRounds"] = f.MaxRounds,
                ["roles"] = new JsonArray(f.Stages.Select(s => (JsonNode?)s.Role).ToArray()),
                ["shared"] = f.Shared,
            });
        return list;
    }

    string NewFlow(ApiArgs a)
    {
        var p = FindProject(a);
        if (p.Agents is null) throw new ApiError($"{p.Name} doesn't use agents.");
        var kind = a.Get("kind") switch
        {
            null or "" => FlowKind.Basic,
            var k when k.Equals("art", StringComparison.OrdinalIgnoreCase) => FlowKind.Art,
            var k when k.Equals("basic", StringComparison.OrdinalIgnoreCase) => FlowKind.Basic,
            var k => throw new ApiError($"kind is basic or art, not \"{k}\"."),
        };
        var shared = a.Get("shared") is { } sh && (sh.Equals("true", StringComparison.OrdinalIgnoreCase) || sh == "1" || sh.Equals("shared", StringComparison.OrdinalIgnoreCase));
        return p.Agents.GenerateFlow(kind, shared, a.Get("description") ?? "").Id;
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
