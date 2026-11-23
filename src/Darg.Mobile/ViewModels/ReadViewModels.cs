using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Darg.Mobile.ViewModels;

// AND-0008: the phone's read-only views of a project: Home (the desktop's overview command), Notes, Docs and
// Reports, each read through the Darg API (notes, docs, doc, reports, report). Nothing here writes: reading a
// report over the API leaves it unread on the PC.

/// <summary>A section's content that reads from the desktop: a status line while it loads or when it can't.</summary>
public abstract partial class ReadSection(ShellViewModel shell, string project) : ObservableObject
{
    protected ShellViewModel Shell { get; } = shell;
    public string Project { get; } = project;

    [ObservableProperty] string _status = "";
    [ObservableProperty] bool _bad;
    [ObservableProperty] bool _busy;
    /// <summary>Whether it has been read once: a section is read when first shown, and again on ⟳.</summary>
    public bool Loaded { get; private set; }
    /// <summary>Handed to the doc and report pages this section opens (see <see cref="ReadPageViewModel.AskFettle"/>).</summary>
    public Action<ReadPageViewModel>? AskFettle { get; set; }

    protected abstract Task Load(DargClient client);

    [RelayCommand]
    public async Task Refresh()
    {
        if (Shell.Client is not { } client || Busy) return;
        Busy = true; Bad = false; Status = "Loading…";
        try { await Load(client); Loaded = true; if (Status == "Loading…") Status = ""; }
        catch (DargClientException e) { Bad = true; Status = e.Message; }
        // anything else (an answer in a shape this build doesn't expect) says so rather than leaving "Loading…" up for good
        catch (Exception e) { Bad = true; Status = $"Couldn't read it: {e.GetType().Name}: {e.Message}"; }
        finally { Busy = false; }
    }

    /// <summary>Read it if it hasn't been yet (a tab's first showing).</summary>
    public void EnsureLoaded() { if (!Loaded && !Busy) _ = Refresh(); }

    /// <summary>A report's kind as its badge: a QA report is "to check", a plain finished one has none.</summary>
    public static string KindBadge(string kind) => kind switch { "qa" => "to check", "finished" => "", _ => kind };

    protected static string S(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    /// <summary>A report's id (its file name without .md) from a path the PC gave, whichever slashes it uses.</summary>
    public static string ReportId(string file)
    {
        var name = file.Split('/', '\\')[^1];
        return name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? name[..^3] : name;
    }
}

/// <summary>A line in a list: what it is, a dim line under it, a badge on the right; tappable when it opens something.</summary>
public sealed record ReadRow(string Title, string Detail = "", string Badge = "", string? Opens = null, bool Strong = false)
{
    public bool CanOpen => Opens is not null;
    public bool HasDetail => Detail.Length > 0;
    public bool HasBadge => Badge.Length > 0;
}

/// <summary>A box on Home: a heading over its rows, or a line saying there's nothing.</summary>
public sealed record HomeTile(string Heading, IReadOnlyList<ReadRow> Rows, string Empty)
{
    public bool IsEmpty => Rows.Count == 0;
}

/// <summary>Home: what's going on in the project, as the desktop's Home shows it (the overview command).</summary>
public sealed partial class HomeViewModel(ShellViewModel shell, string project) : ReadSection(shell, project)
{
    public ObservableCollection<HomeTile> Tiles { get; } = [];
    [ObservableProperty] string _boardLine = "";

    protected override async Task Load(DargClient client)
    {
        var o = await client.Call("overview", new { project = Project }) as JsonObject ?? throw new DargClientException("Darg's overview didn't come back as an object.");
        Tiles.Clear();
        BoardLine = o["columns"] is JsonObject cols
            // the columns holding cards: a row of noughts says nothing on a small screen
            ? $"{S(o["board"])}: " + (cols.Where(c => (int?)c.Value > 0).Select(c => $"{c.Key} {c.Value}").ToList() is { Count: > 0 } held ? string.Join(" · ", held) : "no cards")
            : "";

        if (o["agents"] is JsonArray agents)
            Tiles.Add(new("Agents", agents.OfType<JsonObject>().Select(a => new ReadRow(
                S(a["name"]), S(a["doing"]), (bool?)a["needsYou"] == true ? "needs you" : S(a["state"]), Strong: (bool?)a["needsYou"] == true)).ToList(),
                "No agent at work or waiting on you."));
        if (o["inProgress"] is JsonArray cards)
            Tiles.Add(new("In progress", cards.OfType<JsonObject>().Select(c => new ReadRow(
                $"{S(c["id"])} {S(c["title"])}", c["agent"] is JsonObject ag ? $"{S(ag["name"])} · {S(ag["state"])}" : "no agent", S(c["column"]))).ToList(),
                "Nothing in the middle columns."));
        if (o["unreadReports"] is JsonArray reports)
            Tiles.Add(new("Unread reports", reports.OfType<JsonObject>().Select(r => new ReadRow(
                S(r["title"]), $"{S(r["card"])} · {S(r["date"])}", KindBadge(S(r["kind"])), Opens: ReportId(S(r["file"])))).ToList(),
                "Every report is read."));
        if (o["repos"] is JsonArray repos)
            foreach (var r in repos.OfType<JsonObject>())
            {
                var changed = (int?)r["uncommitted"] ?? 0;
                var head = $"{S(r["name"])} on {S(r["branch"])}" + (changed > 0 ? $", {changed} uncommitted" : "");
                Tiles.Add(new(head, (r["commits"] as JsonArray ?? []).OfType<JsonObject>().Select(c => new ReadRow(
                    S(c["subject"]).Split('.')[0], $"{S(c["hash"])} · {S(c["when"])}")).ToList(), "No commits."));
            }
    }

    [RelayCommand]
    void Open(ReadRow row)
    {
        if (row.Opens is { } id) Shell.Show(new ReportPageViewModel(Shell, Project, id) { AskFettle = AskFettle });
    }
}

/// <summary>A note as the phone shows it: its paper colour, text, and what's under it.</summary>
public sealed record NoteRow(string Id, string Colour, string Text, string Meta)
{
    public bool HasMeta => Meta.Length > 0;
}

/// <summary>A column of the notes wall, as a heading with its notes under it.</summary>
public sealed record NoteGroup(string Column, IReadOnlyList<NoteRow> Notes)
{
    public string Heading => $"{Column} ({Notes.Count})";
}

/// <summary>The notes wall, a column at a time down the page (a wall doesn't fit a phone's width).</summary>
public sealed partial class NotesViewModel(ShellViewModel shell, string project) : ReadSection(shell, project)
{
    public ObservableCollection<NoteGroup> Groups { get; } = [];

    protected override async Task Load(DargClient client)
    {
        var o = await client.Call("notes", new { project = Project }) as JsonObject ?? throw new DargClientException("Darg's notes didn't come back as an object.");
        var columns = (o["columns"] as JsonArray ?? []).Select(S).Where(c => c.Length > 0).ToList();
        var notes = (o["notes"] as JsonArray ?? []).OfType<JsonObject>().Select(n =>
        {
            var tags = (n["tags"] as JsonArray ?? []).Select(t => "#" + S(t));
            var meta = string.Join("  ", new[] { S(n["created"]), S(n["ticket"]) is { Length: > 0 } t ? "→ " + t : "" }.Concat(tags).Where(x => x.Length > 1));
            // as the desktop's wall: a note with a ticket sits in Ticketed, whatever its column says
            var column = S(n["ticket"]).Length > 0 ? "Ticketed" : S(n["column"]);
            return (Column: column, Row: new NoteRow(S(n["id"]), S(n["colour"]), S(n["text"]), meta));
        }).ToList();
        var order = columns.Concat(notes.Select(n => n.Column)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Groups.Clear();
        foreach (var col in order)
        {
            var rows = notes.Where(n => n.Column.Equals(col, StringComparison.OrdinalIgnoreCase)).Select(n => n.Row).ToList();
            if (rows.Count > 0) Groups.Add(new(col, rows));
        }
        if (Groups.Count == 0) Status = "No notes on the wall.";
    }
}

/// <summary>A line of the docs tree: a folder (tap opens or closes it) or a doc (tap reads it), indented by depth.</summary>
public sealed partial class DocEntry(string name, string path, int depth, bool folder) : ObservableObject
{
    public string Name { get; } = name;
    /// <summary>The doc's path for the doc command, or the folder's path (its key for opening).</summary>
    public string Path { get; } = path;
    public int Depth { get; } = depth;
    public bool IsFolder { get; } = folder;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Glyph))] bool _expanded;
    public string Glyph => IsFolder ? (Expanded ? "▾" : "▸") : "▤";
    public Avalonia.Thickness Indent => new(Depth * 20, 0, 0, 8); // the row's own gap below, which a Margin set here replaces
}

/// <summary>The docs folder as the tree it is on disk: docs at the top level showing, folders closed until tapped.</summary>
public sealed partial class DocsViewModel(ShellViewModel shell, string project) : ReadSection(shell, project)
{
    List<DocEntry> _all = [];
    public ObservableCollection<DocEntry> Visible { get; } = [];

    protected override async Task Load(DargClient client)
    {
        var list = await client.Call("docs", new { project = Project }) as JsonArray ?? throw new DargClientException("Darg's docs didn't come back as a list.");
        var open = _all.Where(e => e.IsFolder && e.Expanded).Select(e => e.Path).ToHashSet();
        _all = Tree(list.Select(S).Where(p => p.Length > 0));
        foreach (var f in _all.Where(e => e.IsFolder && open.Contains(e.Path))) f.Expanded = true;
        Sync();
        if (_all.Count == 0) Status = "No docs in the docs folder.";
    }

    /// <summary>Paths to tree lines: each folder before what's in it, folders before docs at each level.</summary>
    public static List<DocEntry> Tree(IEnumerable<string> paths)
    {
        var docs = paths.ToList();
        var result = new List<DocEntry>();
        void Level(string prefix, int depth)
        {
            var here = docs.Where(p => p.StartsWith(prefix, StringComparison.Ordinal)).Select(p => p[prefix.Length..]).ToList();
            foreach (var folder in here.Where(p => p.Contains('/')).Select(p => p[..p.IndexOf('/')]).Distinct().Order(StringComparer.OrdinalIgnoreCase))
            {
                result.Add(new DocEntry(folder, prefix + folder, depth, true));
                Level(prefix + folder + "/", depth + 1);
            }
            foreach (var file in here.Where(p => !p.Contains('/')).Order(StringComparer.OrdinalIgnoreCase))
                result.Add(new DocEntry(file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? file[..^3] : file, prefix + file, depth, false));
        }
        Level("", 0);
        return result;
    }

    void Sync()
    {
        Visible.Clear();
        foreach (var e in _all)
        {
            // shown when every folder above it is open
            var parents = _all.Where(f => f.IsFolder && e.Path.StartsWith(f.Path + "/", StringComparison.Ordinal));
            if (parents.All(f => f.Expanded)) Visible.Add(e);
        }
    }

    [RelayCommand]
    public void Open(DocEntry entry)
    {
        if (entry.IsFolder) { entry.Expanded = !entry.Expanded; Sync(); }
        else Shell.Show(new DocPageViewModel(Shell, Project, entry.Path) { AskFettle = AskFettle });
    }
}

/// <summary>A screen that reads one thing (a doc, a report) and follows its links.</summary>
public abstract partial class ReadPageViewModel(ShellViewModel shell, string project) : PageViewModel
{
    protected ShellViewModel Shell { get; } = shell;
    public string Project { get; } = project;

    [ObservableProperty] string _status = "Loading…";
    [ObservableProperty] bool _bad;
    [ObservableProperty] string _text = "";
    /// <summary>A dim block above the text (a report's card, date and commits); empty for a doc.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasDetails))] string _details = "";
    public bool HasDetails => Details.Length > 0;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Title))] string _heading = "";
    public override string Title => Heading;

    /// <summary>Opens the project's Fettle chat from this page (its ✦ Fettle button), told what's open here; given by
    /// the project page that opened it, so links followed from here pass it on.</summary>
    public Action<ReadPageViewModel>? AskFettle { get; init; }
    public bool CanTalkToFettle => AskFettle is not null;
    /// <summary>The doc this page shows, as Fettle is told it; null for a report.</summary>
    public virtual string? DocPath => null;

    [RelayCommand]
    void Fettle() => AskFettle?.Invoke(this);

    protected abstract Task Load(DargClient client);

    public async Task Refresh()
    {
        if (Shell.Client is not { } client) return;
        try { await Load(client); Status = ""; Bad = false; }
        catch (DargClientException e) { Bad = true; Status = e.Message; }
        catch (Exception e) { Bad = true; Status = $"Couldn't read it: {e.GetType().Name}: {e.Message}"; }
    }

    protected static bool IsWeb(string target) =>
        target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    /// <summary>A tapped link: the address for the phone to open when it's on the web, else null (having opened a doc
    /// or said in the status line why it can't).</summary>
    public virtual Uri? Follow(string target)
    {
        if (IsWeb(target) && Uri.TryCreate(target, UriKind.Absolute, out var web)) return web;
        Say(Core.CardLinks.IdOf(target) is { } card ? $"{card} is a card: find it on the Board." : $"{Shown(target)} opens on the PC only.");
        return null;
    }

    protected static string Shown(string target) => target.StartsWith("wiki:", StringComparison.Ordinal) ? target[5..] : target;

    protected void Say(string message) { Bad = false; Status = message; }

    /// <summary>The header's title follows the page (the shell copies it when the page shows).</summary>
    partial void OnHeadingChanged(string value) => Shell.Retitle(this);
}

/// <summary>One doc, drawn as markdown, its [[links]] and relative links opening the doc they name.</summary>
public sealed partial class DocPageViewModel : ReadPageViewModel
{
    public string Path { get; }
    public override string? DocPath => Path;
    JsonObject _links = [];

    public DocPageViewModel(ShellViewModel shell, string project, string path) : base(shell, project)
    {
        Path = path;
        Heading = path.Split('/')[^1] is var f && f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? f[..^3] : path;
        _ = Refresh();
    }

    protected override async Task Load(DargClient client)
    {
        var o = await client.Call("doc", new { project = Project, path = Path }) as JsonObject ?? throw new DargClientException("Darg's doc didn't come back as an object.");
        _links = o["links"] as JsonObject ?? [];
        Heading = (string?)o["title"] ?? Heading;
        Text = (string?)o["text"] ?? "";
    }

    public override Uri? Follow(string target)
    {
        if (_links[target] is not JsonObject link) return base.Follow(target);
        switch ((string?)link["kind"])
        {
            case "doc": Shell.Show(new DocPageViewModel(Shell, Project, (string?)link["path"] ?? "") { AskFettle = AskFettle }); return null;
            case "external" when Uri.TryCreate((string?)link["target"], UriKind.Absolute, out var u): return u;
            case "elsewhere": Say($"{(string?)link["name"]} is outside the docs folder: it opens on the PC only."); return null;
            case "missing": Say($"{Shown(target)}: {(string?)link["reason"] ?? "no such doc"}."); return null;
            default: return base.Follow(target);
        }
    }
}

/// <summary>The project's finished-agent reports, newest first, unread ones marked.</summary>
public sealed partial class ReportsViewModel(ShellViewModel shell, string project) : ReadSection(shell, project)
{
    public ObservableCollection<ReadRow> Reports { get; } = [];

    protected override async Task Load(DargClient client)
    {
        var list = await client.Call("reports", new { project = Project }) as JsonArray ?? throw new DargClientException("Darg's reports didn't come back as a list.");
        Reports.Clear();
        foreach (var r in list.OfType<JsonObject>())
        {
            var unread = (bool?)r["read"] == false;
            Reports.Add(new ReadRow((unread ? "● " : "") + S(r["title"]), $"{S(r["card"])} · {S(r["date"])}", KindBadge(S(r["kind"])), Opens: S(r["id"]), Strong: unread));
        }
        if (Reports.Count == 0) Status = "No reports yet.";
    }

    [RelayCommand]
    void Open(ReadRow row)
    {
        if (row.Opens is { } id) Shell.Show(new ReportPageViewModel(Shell, Project, id) { AskFettle = AskFettle });
    }
}

/// <summary>One report: what it's about above its markdown body.</summary>
public sealed partial class ReportPageViewModel : ReadPageViewModel
{
    public string Id { get; }

    public ReportPageViewModel(ShellViewModel shell, string project, string id) : base(shell, project)
    {
        Id = id;
        Heading = "Report";
        _ = Refresh();
    }

    protected override async Task Load(DargClient client)
    {
        var o = await client.Call("report", new { project = Project, id = Id }) as JsonObject ?? throw new DargClientException("Darg's report didn't come back as an object.");
        string S(string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        Heading = S("title") is { Length: > 0 } t ? t : Id;
        var commits = (o["commits"] as JsonArray ?? []).Select(c => (string?)c).Where(c => !string.IsNullOrEmpty(c)).ToList();
        Details = string.Join("\n", new[]
        {
            string.Join(" · ", new[] { S("card"), ReadSection.KindBadge(S("kind")), S("date") }.Where(x => x.Length > 0)),
            commits.Count > 0 ? "Commits: " + string.Join(" ", commits) : "",
            S("repo").Length > 0 ? "Repo: " + S("repo") : "",
        }.Where(x => x.Length > 0));
        Text = S("body");
    }
}
