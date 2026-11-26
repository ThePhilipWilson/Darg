using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Headless;
using Avalonia.Threading;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>Darg as Philip runs it, on a throwaway instance: one project ("Proj") with a git repo and a board
/// in his columns, the real MainWindow drawn headless, agents played by the fake claude. Each test gets a
/// fresh folder, .shots/integration/&lt;test&gt;, which is left there afterwards to look at: the instance, the
/// board files, fake-claude.jsonl (what every agent was started with and sent) and the pictures taken.
///
/// Make one, add cards, then Start:
///   using var app = DargApp.Make();
///   var card = app.Card("fix the logo", "QA Ready");
///   await app.Start();
///   await app.Api("send-back", new { project = "proj", card = card.Id, notes = "still jagged" });</summary>
public sealed class DargApp : IDisposable
{
    public static readonly string[] Columns = ["To Do", "In Progress", "QA Ready", BoardStore.AttentionName, "needs more work", "Done"];

    public string Name { get; }
    /// <summary>.shots/integration/&lt;test&gt;.</summary>
    public string Folder { get; }
    public string Home { get; }
    public string Repo { get; }
    public string Boards { get; }
    /// <summary>The project's one board, Main.</summary>
    public string BoardFolder { get; }
    /// <summary>The project's docs folder, with a PLAN.md.</summary>
    public string Docs { get; }
    /// <summary>A JSON line per fake claude start, message and one-shot question (tools/perf/fake_claude.py).</summary>
    public string ClaudeLog { get; }

    MainWindow? _window;
    public MainWindow Window => _window ?? throw new InvalidOperationException("Start the app first.");
    public MainViewModel Main => (MainViewModel)Window.DataContext!;
    public ProjectViewModel Project => Main.Projects.Single();
    public BoardViewModel Board => Project.Board ?? throw new InvalidOperationException("The project has no board.");
    public AgentsViewModel Agents => Project.Agents ?? throw new InvalidOperationException("The project has no agents.");

    DargApp(string name)
    {
        Name = name;
        Folder = Path.Combine(Sandbox.Root, name);
        Wipe(Folder);
        Home = Directory.CreateDirectory(Path.Combine(Folder, "home")).FullName;
        Repo = Path.GetFullPath(Path.Combine(Folder, "proj", "repo"));
        Boards = Path.Combine(Folder, "proj", "tasks");
        ClaudeLog = Path.Combine(Folder, "fake-claude.jsonl");
        Copy(TemplateRepo.Value, Repo);
        BoardFolder = BoardStore.CreateBoard(Boards, "Main", Columns, "T");
        Docs = Directory.CreateDirectory(Path.Combine(Folder, "proj", "docs")).FullName;
        File.WriteAllText(Path.Combine(Docs, "PLAN.md"), "# Plan\n\nWhat Proj is for, and what comes next.\n");
        var config = new JsonObject
        {
            ["name"] = "tests",
            ["projects"] = new JsonArray(new JsonObject
            {
                ["id"] = "proj", ["name"] = "Proj", ["color"] = "#e07ab8",
                ["boards"] = Boards, ["repos"] = new JsonArray(Repo), ["docs"] = Docs,
            }),
        };
        File.WriteAllText(Path.Combine(Home, "instance.json"), config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>A fresh instance for the calling test, not started yet.</summary>
    public static DargApp Make([CallerMemberName] string test = "") => new(test);

    /// <summary>Fake agents started from now on keep a turn going while held (up to 60 s) and end it at their next step
    /// once released (tools/perf/fake_claude.py's FAKE_HOLD), so a test that catches an agent mid-turn and then waits
    /// for its turn to end waits only as long as it needs (WOR-0226), not out a fixed FAKE_SECONDS. Disposing it
    /// releases and puts the Sandbox's 1 s turns back.</summary>
    /// <param name="name">A hold of its own for the agents started from now on, so two agents' turns end apart.</param>
    public TurnHold HoldTurns(bool held = true, string name = "turn-hold") => new(Path.Combine(Folder, name), held);

    public sealed class TurnHold : IDisposable
    {
        readonly string _file;

        internal TurnHold(string file, bool held)
        {
            _file = file;
            if (held) Hold(); else Release();
            Environment.SetEnvironmentVariable("FAKE_HOLD", file);
            Environment.SetEnvironmentVariable("FAKE_SECONDS", "60");
        }

        /// <summary>Turns go on until <see cref="Release"/>; a turn already over isn't started again.</summary>
        public void Hold() => File.WriteAllText(_file, "");

        /// <summary>Every held turn ends at its next step (within a quarter second); turns started now take one step.</summary>
        public void Release() { if (File.Exists(_file)) File.Delete(_file); }

        public void Dispose()
        {
            Release();
            Environment.SetEnvironmentVariable("FAKE_HOLD", null);
            Environment.SetEnvironmentVariable("FAKE_SECONDS", "1");
        }
    }

    /// <summary>A card on the board, in a column (by name; Backlog by default).</summary>
    public Card Card(string title, string column = "Backlog", string description = "", string type = "Task")
    {
        var card = BoardStore.Create(BoardFolder, title, description, type: type);
        if (column != "Backlog") BoardStore.Move(BoardFolder, card.Id, ColumnId(column));
        return BoardStore.ReadCards(BoardFolder).Single(c => c.Id == card.Id);
    }

    /// <summary>A board column's list id, by name.</summary>
    public string ColumnId(string name) => BoardStore.Load(BoardFolder).Columns.First(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Id;

    /// <summary>The card as it is on disk now.</summary>
    public Card Read(string id) => BoardStore.ReadCards(BoardFolder).Single(c => c.Id == id);

    /// <summary>Open the app on this instance: the main window, shown, with its view model.</summary>
    public async Task Start()
    {
        Environment.SetEnvironmentVariable("DARG_HOME", Home);
        Environment.SetEnvironmentVariable("FAKE_LOG", ClaudeLog);
        Sandbox.Check();
        lock (Sandbox.Notifications) Sandbox.Notifications.Clear();
        _window = new MainWindow { Width = 1400, Height = 900 };
        _window.Show();
        await Settle();
    }

    /// <summary>Run one of the app's Darg API commands, as Fettle or a script would. Throws on an ApiError (a 400).</summary>
    public async Task<string> Api(string command, object? args = null)
    {
        var server = ArtifactHost.Server ?? throw new InvalidOperationException("The artifact server (and with it the API) didn't start.");
        var (status, body) = await server.Api.Call(command, ApiArgs.Parse(JsonSerializer.Serialize(args ?? new { })));
        if (status != 200) throw new InvalidOperationException($"{command}: {status} {body}");
        await Settle();
        return body;
    }

    /// <summary>Let the UI catch up: queued work, bindings, layout and a frame.</summary>
    public static async Task Settle(int ms = 50)
    {
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(ms);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    /// <summary>Wait until something is true, letting the app run meanwhile; fails the test with <paramref name="what"/> if it never is.</summary>
    public static async Task Wait(Func<bool> condition, string what, int seconds = 30)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > end) throw new TimeoutException($"Waited {seconds} s for: {what}");
            await Settle(100);
        }
    }

    /// <summary>As Wait, but a timeout says what the app was doing: so a failure can be read, not rerun.</summary>
    public async Task Until(Func<bool> condition, string what, int seconds = 30)
    {
        try { await Wait(condition, what, seconds); }
        catch (TimeoutException e) { throw new TimeoutException(e.Message + Environment.NewLine + Diagnose(), e); }
    }

    /// <summary>The agents (state, what they're doing, their transcript's last lines), what the fake claude logged, and crash.log.</summary>
    public string Diagnose()
    {
        var s = new System.Text.StringBuilder();
        foreach (var a in _window is null ? [] : Agents.Agents)
        {
            s.AppendLine($"agent {a.Id} \"{a.Name}\" card {a.CardId} purpose {a.Purpose}: {a.State} ({a.StateText}) {a.Activity}");
            foreach (var item in a.Items.TakeLast(5))
                s.AppendLine("  " + Clip(item switch
                {
                    UserItem u => "you: " + u.Text, TextItem t => "agent: " + t.Text,
                    _ => item.GetType().Name + ": " + item.GetType().GetProperty("Text")?.GetValue(item),
                }, 200));
        }
        s.AppendLine("fake claude log:");
        foreach (var c in ClaudeCalls()) s.AppendLine($"  {c["kind"]} pid {c["pid"]}: {Clip((string?)c["text"] ?? "", 120)}");
        if (Crashes() is { Length: > 0 } crash) s.AppendLine("crash.log:" + Environment.NewLine + crash);
        return s.ToString();
    }

    static string Clip(string text, int length)
    {
        text = text.ReplaceLineEndings(" ");
        return text.Length <= length ? text : text[..length] + "…";
    }

    /// <summary>Save what the window shows as a PNG in this test's folder; returns its path. Agents doing QA
    /// open these to see a change without a running app.</summary>
    public string Shot(string name)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nothing was drawn.");
        var path = Path.Combine(Folder, name + ".png");
#pragma warning disable CS0618 // the newer overload wants encoder options; a plain PNG is all this needs
        frame.Save(path);
#pragma warning restore CS0618
        return path;
    }

    /// <summary>What the fake claude logged: each entry's kind (start, message, ask) and fields.</summary>
    public IReadOnlyList<JsonObject> ClaudeCalls()
    {
        var calls = new List<JsonObject>();
        foreach (var line in ReadShared(ClaudeLog).Split('\n'))
        {
            if (line.Trim().Length == 0) continue;
            try { calls.Add(JsonNode.Parse(line)!.AsObject()); }
            catch (JsonException) { } // the last line, half written as we read: the next look gets it whole
        }
        return calls;
    }

    /// <summary>A file another process may have open to write (fake claude appending to its log): read with
    /// read/write sharing, as File.ReadAllLines doesn't (it refused while python had the log open and failed a
    /// test, WOR-0127), retrying a moment if Windows still says no.</summary>
    static string ReadShared(string path)
    {
        for (var tries = 0; ; tries++)
        {
            try
            {
                if (!File.Exists(path)) return "";
                using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var r = new StreamReader(f);
                return r.ReadToEnd();
            }
            catch (IOException) when (tries < 50) { Thread.Sleep(20); }
        }
    }

    /// <summary>The messages agents were sent, in order (a card agent's first is its prompt).</summary>
    public IReadOnlyList<string> Prompts() => ClaudeCalls().Where(c => (string?)c["kind"] == "message").Select(c => (string)c["text"]!).ToList();

    /// <summary>What went wrong in a view (crash.log in the instance), or empty.</summary>
    public string Crashes() => ReadShared(Path.Combine(Home, "crash.log"));

    /// <summary>Delete the last run's folder; git keeps its objects read-only, which Directory.Delete refuses.</summary>
    static void Wipe(string folder)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(folder, true);
    }

    /// <summary>The project's repo as every test starts it (a README, one commit), made once a run and copied into each
    /// test's folder: three git processes a test cost 0.3 to 1.9 s, about two minutes a run (WOR-0226).</summary>
    static readonly Lazy<string> TemplateRepo = new(() =>
    {
        var repo = Path.Combine(Sandbox.RunFolder, "_template", "repo");
        Wipe(Path.GetDirectoryName(repo)!);
        Directory.CreateDirectory(repo);
        Git(repo, "init", "-q", "-b", "main");
        File.WriteAllText(Path.Combine(repo, "README.md"), "# Proj\n\nA throwaway project for Darg's integration tests.\n");
        Git(repo, "add", "README.md");
        Git(repo, "-c", "user.name=Darg tests", "-c", "user.email=tests@example.com", "commit", "-q", "-m", "First");
        return repo;
    });

    static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
    }

    static void Git(string repo, params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = repo, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', args)}: {p.StandardError.ReadToEnd()}");
    }

    /// <summary>Darg closing as a crash or an update leaves it, then opening again on the same instance (WOR-0221). Every
    /// agent is saved as it stands (what the window's Closing does), its saved files are kept as they are at that moment,
    /// the app is closed, the files are put back as they were (a crash writes nothing after; Darg's own close of a session
    /// doesn't either), <paramref name="whileClosed"/> runs, and a new window opens.</summary>
    public async Task Reopen(Action? whileClosed = null)
    {
        Main.SaveAll();
        AgentStore.Flush();
        var store = AgentStore.Folder("proj", Home);
        var saved = Directory.Exists(store) ? Directory.GetFiles(store, "*.json").ToDictionary(f => f, File.ReadAllText) : [];
        Close();
        AgentStore.Flush();
        foreach (var f in Directory.Exists(store) ? Directory.GetFiles(store, "*.json") : []) File.Delete(f);
        foreach (var (f, text) in saved) File.WriteAllText(f, text);
        whileClosed?.Invoke();
        await Start();
    }

    public void Dispose() => Close();

    void Close()
    {
        if (_window is null) return;
        // close the projects, not just their agents' sessions: the window closing leaves their view models, board
        // watchers and timers alive in the shared app, and work they had queued (a send-back's rework agent still
        // picking its model, a QA agent due when a turn ends) started agents during the next test, logging into its
        // FAKE_LOG (WOR-0131). Close ends every agent, those mid-pick too, stops the watchers and starts nothing after.
        foreach (var p in Main.Projects.ToList()) p.Close();
        foreach (var a in Main.Mascot.Live().ToList()) a.Shut();
        _window.Close();
        _window = null;
        Dispatcher.UIThread.RunJobs();
    }
}
