using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Darg.Mobile.ViewModels;

/// <summary>A screen of the phone app: what the header shows above it.</summary>
public abstract partial class PageViewModel : ObservableObject
{
    public abstract string Title { get; }
}

/// <summary>The phone app's root (AND-0003): a stack of screens with a back step, starting at the project list when
/// a connection is saved and at the connection screen when not. Android's back button calls <see cref="Back"/>.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    readonly Stack<PageViewModel> _stack = new();
    readonly Func<Connection, DargClient> _clientFor;

    public ConnectionStore Store { get; }
    public DargClient? Client { get; private set; }

    [ObservableProperty] PageViewModel? _page;
    [ObservableProperty] bool _canGoBack;
    [ObservableProperty] string _title = "Darg";

    /// <summary>Whether the header offers the connection screen (everywhere but on it).</summary>
    [ObservableProperty] bool _canOpenSettings;

    public ShellViewModel(ConnectionStore store, Func<Connection, DargClient>? clientFor = null)
    {
        Store = store;
        _clientFor = clientFor ?? (c => new DargClient(c));
        var saved = store.Load();
        if (saved is { IsComplete: true })
        {
            Client = _clientFor(saved);
            var projects = new ProjectsViewModel(this);
            Show(projects);
            _ = projects.Refresh();
        }
        else Show(new ConnectViewModel(this, saved));
    }

    /// <summary>Saves a connection that has just answered and goes to its projects (the list it fetched, so no second call).</summary>
    internal void Connected(Connection c, DargClient client, IReadOnlyList<ProjectInfo> projects)
    {
        Store.Save(c);
        if (!ReferenceEquals(Client, client)) Client?.Dispose();
        Client = client;
        _stack.Clear();
        var page = new ProjectsViewModel(this);
        page.Fill(projects);
        Show(page);
    }

    internal DargClient ClientFor(Connection c) => _clientFor(c);

    public void Show(PageViewModel page)
    {
        _stack.Push(page);
        Sync();
    }

    /// <summary>One screen back; false at the first screen, so Android's back button can leave the app there.</summary>
    [RelayCommand]
    void GoBack() => Back();

    public bool Back()
    {
        if (_stack.Count <= 1) return false;
        _stack.Pop();
        Sync();
        return true;
    }

    [RelayCommand]
    void OpenSettings()
    {
        if (Page is ConnectViewModel) return;
        Show(new ConnectViewModel(this, Store.Load() ?? Client?.Connection));
    }

    void Sync()
    {
        Page = _stack.Peek();
        Title = Page.Title;
        CanGoBack = _stack.Count > 1;
        CanOpenSettings = Page is not ConnectViewModel;
    }
}

/// <summary>The connection screen: the desktop's address and token, and a Connect that proves them by listing projects.</summary>
public sealed partial class ConnectViewModel : PageViewModel
{
    readonly ShellViewModel _shell;

    public override string Title => "Connect to Darg";

    [ObservableProperty] string _address;
    [ObservableProperty] string _token;
    [ObservableProperty] string _status = "";
    [ObservableProperty] bool _bad;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ConnectCommand))] bool _busy;

    public string Help =>
        "Over USB: run pwsh -File tools/android.ps1 -NoBuild on the PC; it forwards the port and fills both boxes in. " +
        "Otherwise paste the address Darg gives for its API (http://<pc>:47817/api/<token>), which carries the token too.";

    public ConnectViewModel(ShellViewModel shell, Connection? current)
    {
        _shell = shell;
        _address = current?.Address is { Length: > 0 } a ? a : Connection.DefaultAddress;
        _token = current?.Token ?? "";
    }

    /// <summary>A pasted API address carries the token: split it into the two boxes as it lands.</summary>
    partial void OnAddressChanged(string value)
    {
        // only a whole token (Darg's are 32 hex digits), so one being typed into the box isn't snatched away mid-word
        if (Connection.ParseAddress(value) is { Token: { Length: >= 32 } token } parsed)
        {
            Token = token;
            Address = parsed.Address;
        }
    }

    bool CanConnect() => !Busy;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    public async Task Connect()
    {
        if (Connection.ParseAddress(Address) is not { } parsed) { Fail("That isn't an address: try http://localhost:47817."); return; }
        var c = new Connection(parsed.Address, (parsed.Token ?? Token).Trim());
        if (c.Token.Length == 0) { Fail("The token is missing: it's in the address Darg gives for its API, after /api/."); return; }
        Busy = true; Bad = false; Status = $"Connecting to {c.Address}…";
        var client = _shell.ClientFor(c);
        try
        {
            var projects = await client.Projects();
            Status = $"Connected: {projects.Count} project{(projects.Count == 1 ? "" : "s")}.";
            _shell.Connected(c, client, projects);
        }
        catch (DargClientException e) { client.Dispose(); Fail(e.Message); }
        finally { Busy = false; }
    }

    void Fail(string message) { Bad = true; Status = message; }
}

/// <summary>The desktop's projects; tapping one opens it.</summary>
public sealed partial class ProjectsViewModel(ShellViewModel shell) : PageViewModel
{
    public override string Title => "Projects";

    public ObservableCollection<ProjectInfo> Projects { get; } = [];

    [ObservableProperty] string _status = "";
    [ObservableProperty] bool _bad;
    [ObservableProperty] bool _busy;

    public string Where => shell.Client?.Connection.Address ?? "";

    internal void Fill(IReadOnlyList<ProjectInfo> projects)
    {
        Projects.Clear();
        foreach (var p in projects) Projects.Add(p);
        Bad = false;
        Status = projects.Count == 0 ? "Darg has no projects yet." : "";
    }

    [RelayCommand]
    public async Task Refresh()
    {
        if (shell.Client is not { } client || Busy) return;
        Busy = true; Status = "Loading…"; Bad = false;
        try { Fill(await client.Projects()); }
        catch (DargClientException e) { Bad = true; Status = e.Message; }
        finally { Busy = false; }
    }

    [RelayCommand]
    void Open(ProjectInfo project) => shell.Show(new ProjectPageViewModel(project, shell));
}

/// <summary>A section the phone has for a project, and the card that fills it in.</summary>
public sealed record PhoneSection(string Name, string Glyph, string Card, string What);

/// <summary>A project: its phone sections along the bottom, the picked one above (each a placeholder until its card lands).</summary>
public sealed partial class ProjectPageViewModel : PageViewModel
{
    /// <summary>What the phone offers of a project (docs/ANDROID.md's v1), in bar order. Board, Notes and Docs only
    /// where the desktop says the project has them; Terminal, Code, Preview and the rest stay desktop-only.</summary>
    public static readonly IReadOnlyList<PhoneSection> All =
    [
        new("Board", "▦", "AND-0004", "this project's boards, one list at a time"),
        new("Agents", "◉", "AND-0005", "this project's agents and their transcripts"),
        new("Notes", "✎", "AND-0008", "the notes wall"),
        new("Docs", "▤", "AND-0008", "this project's docs"),
        new("Reports", "✓", "AND-0008", "finished agents' reports"),
    ];

    static readonly HashSet<string> Always = ["Agents", "Reports"];

    public ProjectInfo Project { get; }
    public IReadOnlyList<SectionTab> Tabs { get; }
    public override string Title => Project.Name;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Placeholder))] PhoneSection _section;

    public string Placeholder => $"Coming with {Section.Card}: {Section.What}.";

    readonly ShellViewModel? _shell;
    FettleChatViewModel? _fettle;

    /// <summary>The card or doc open in the section showing, which Fettle is told about: set by the section that opens
    /// one (AND-0004's card panel, AND-0008's doc reader).</summary>
    [ObservableProperty] string? _openCard;
    [ObservableProperty] string? _openDoc;

    /// <summary>What this page shows, as Fettle is told it with each message.</summary>
    public PhoneView Showing => new(Section.Name, OpenCard, OpenDoc);

    public bool CanTalkToFettle => _shell?.Client is not null;

    public ProjectPageViewModel(ProjectInfo project, ShellViewModel? shell = null)
    {
        Project = project;
        _shell = shell;
        Tabs = All.Where(s => Always.Contains(s.Name) || project.Sections.Contains(s.Name, StringComparer.OrdinalIgnoreCase))
                  .Select(s => new SectionTab(s)).ToList();
        _section = Tabs[0].Section;
        Tabs[0].On = true;
    }

    partial void OnSectionChanged(PhoneSection value)
    {
        foreach (var t in Tabs) t.On = t.Section == value;
    }

    [RelayCommand]
    void Pick(SectionTab tab) => Section = tab.Section;

    /// <summary>Opens Fettle full screen (AND-0007); the same chat each time for this page, so it carries on.</summary>
    [RelayCommand]
    public void Fettle()
    {
        if (_shell?.Client is not { } client) return;
        _fettle ??= FettleChatViewModel.For(client, Project, () => Showing);
        _shell.Show(_fettle);
    }
}

/// <summary>A section's button in the bottom bar, lit when it's the one showing.</summary>
public sealed partial class SectionTab(PhoneSection section) : ObservableObject
{
    public PhoneSection Section { get; } = section;
    [ObservableProperty] bool _on;
}
