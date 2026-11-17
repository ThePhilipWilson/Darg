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
        Repo = Directory.CreateDirectory(Path.Combine(Folder, "proj", "repo")).FullName;
        Boards = Path.Combine(Folder, "proj", "tasks");
        ClaudeLog = Path.Combine(Folder, "fake-claude.jsonl");
        Git("init", "-q", "-b", "main");
        File.WriteAllText(Path.Combine(Repo, "README.md"), "# Proj\n\nA throwaway project for Darg's integration tests.\n");
        Git("add", "README.md");
        Git("-c", "user.name=Darg tests", "-c", "user.email=tests@example.com", "commit", "-q", "-m", "First");
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
    public static async Task Until(Func<bool> condition, string what, int seconds = 30)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > end) throw new TimeoutException($"Waited {seconds} s for: {what}");
            await Settle(100);
        }
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
    public IReadOnlyList<JsonObject> ClaudeCalls() => !File.Exists(ClaudeLog) ? []
        : File.ReadAllLines(ClaudeLog).Where(l => l.Length > 0).Select(l => JsonNode.Parse(l)!.AsObject()).ToList();

    /// <summary>The messages agents were sent, in order (a card agent's first is its prompt).</summary>
    public IReadOnlyList<string> Prompts() => ClaudeCalls().Where(c => (string?)c["kind"] == "message").Select(c => (string)c["text"]!).ToList();

    /// <summary>What went wrong in a view (crash.log in the instance), or empty.</summary>
    public string Crashes() => File.Exists(Path.Combine(Home, "crash.log")) ? File.ReadAllText(Path.Combine(Home, "crash.log")) : "";

    /// <summary>Delete the last run's folder; git keeps its objects read-only, which Directory.Delete refuses.</summary>
    static void Wipe(string folder)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(folder, true);
    }

    void Git(params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = Repo, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', args)}: {p.StandardError.ReadToEnd()}");
    }

    public void Dispose()
    {
        if (_window is null) return;
        foreach (var a in Agents.Agents.ToList()) a.CloseSession("tests");
        _window.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
