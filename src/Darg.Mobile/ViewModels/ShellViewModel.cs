using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Darg.Mobile.ViewModels;

/// <summary>A screen of the phone app: what the header shows above it.</summary>
public abstract partial class PageViewModel : ObservableObject
{
    public abstract string Title { get; }

    /// <summary>Android's back on this page: true when the page dealt with it itself (closed something open on it).</summary>
    public virtual bool HandleBack() => false;
}

/// <summary>The phone app's root (AND-0003): a stack of screens with a back step, starting at Needs you (AND-0010; the
/// projects are a step on from it) when a connection is saved and at the connection screen when not. Android's back button calls <see cref="Back"/>.</summary>
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
            Show(new NeedsYouViewModel(this)); // its view reads the desktop as it comes on screen
        }
        else Show(new ConnectViewModel(this, saved));
    }

    /// <summary>Saves a connection that has just answered and goes to the first screen, Needs you (AND-0010), with the
    /// projects a step on from it (the list it fetched shows there at once).</summary>
    internal void Connected(Connection c, DargClient client, IReadOnlyList<ProjectInfo> projects)
    {
        Store.Save(c);
        if (!ReferenceEquals(Client, client)) Client?.Dispose();
        Client = client;
        _stack.Clear();
        Show(new NeedsYouViewModel(this, projects));
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
        if (Page?.HandleBack() == true) return true;
        if (_stack.Count <= 1) return false;
        _stack.Pop();
        Sync();
        return true;
    }

    /// <summary>A pairing link has arrived (scanned in the app, or opened from the phone's camera): open the connection
    /// screen with it filled in and pair at once.</summary>
    public Task PairWith(string link)
    {
        if (Page is not ConnectViewModel connect)
        {
            connect = new ConnectViewModel(this, Store.Load() ?? Client?.Connection);
            Show(connect);
        }
        connect.PairText = link;
        return connect.Pair();
    }

    /// <summary>Forgets this phone's connection and goes back to the connection screen. The desktop still lists the phone
    /// until it's revoked in Settings; pairing again makes a new entry.</summary>
    internal void Forget()
    {
        Store.Clear();
        Client?.Dispose();
        Client = null;
        _stack.Clear();
        Show(new ConnectViewModel(this, null));
    }

    /// <summary>A page's title changed after it showed (a doc's real title once it's read): the header follows.</summary>
    internal void Retitle(PageViewModel page)
    {
        if (ReferenceEquals(Page, page)) Title = page.Title;
    }

    [RelayCommand]
    void OpenSettings() => OpenSettingsPage();

    internal void OpenSettingsPage()
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
    [ObservableProperty] string _pairText = "";
    [ObservableProperty] string _status = "";
    [ObservableProperty] bool _bad;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(PairCommand))] bool _busy;

    /// <summary>How the phone is connected now, when it is (the pairing, certificate and address), else empty.</summary>
    public string Current { get; }

    /// <summary>Whether this phone has a connection saved, for the Forget button.</summary>
    public bool HasConnection => Current.Length > 0;

    /// <summary>Whether the host can scan a QR in the app (Android sets <see cref="App.Scanner"/>).</summary>
    public bool CanScan => App.Scanner is not null;

    public string PairHelp =>
        "On the PC: Settings → Phone → Pair a phone shows a QR. Scan it here (or with the camera, which opens this app), " +
        "or paste the darg://pair link. The phone then trusts that PC's certificate and no other.";

    public string Help =>
        "Over USB: run pwsh -File tools/android.ps1 -NoBuild on the PC; it forwards the port and fills both boxes in. " +
        "Otherwise paste the address Darg gives for its API (http://<pc>:47817/api/<token>), which carries the token too.";

    public ConnectViewModel(ShellViewModel shell, Connection? current)
    {
        _shell = shell;
        _address = current?.Address is { Length: > 0 } a ? a : Connection.DefaultAddress;
        _token = current?.Token ?? "";
        Current = current is { IsComplete: true } ? current.Route : "";
    }

    [RelayCommand]
    async Task Scan()
    {
        if (App.Scanner is not { } scan) return;
        try
        {
            var text = await scan();
            if (string.IsNullOrWhiteSpace(text)) return; // cancelled
            PairText = text;
            await Pair();
        }
        catch (Exception e) when (e is not OperationCanceledException) { Fail($"The scanner didn't work: {e.Message}. Paste the link instead."); }
    }

    [RelayCommand]
    void Forget() => _shell.Forget();

    bool CanPair() => !Busy;

    /// <summary>Pairs with the link in the box: the code for a token over TLS pinned to the QR's certificate, kept with
    /// the pin; then the project list proves it.</summary>
    [RelayCommand(CanExecute = nameof(CanPair))]
    public async Task Pair()
    {
        if (PairingLink.Parse(PairText) is not { } link)
        {
            Fail("That isn't a Darg pairing link: it starts darg://pair and comes from the QR in Darg's Settings → Phone.");
            return;
        }
        Busy = true; Bad = false; Status = $"Pairing with {link.Name}…";
        DargClient? client = null;
        try
        {
            var c = await Pairer.Pair(link, App.DeviceName);
            client = _shell.ClientFor(c);
            var projects = await client.Projects();
            PairText = "";
            Status = $"Paired with {c.Name}: {projects.Count} project{(projects.Count == 1 ? "" : "s")}.";
            _shell.Connected(c, client, projects);
        }
        catch (DargClientException e) { client?.Dispose(); Fail(e.Message); }
        finally { Busy = false; }
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

    public string Where => shell.Client?.Connection.Route ?? "";

    /// <summary>The desktop no longer knows this phone (revoked): Pair again goes to the connection screen.</summary>
    [ObservableProperty] bool _unpaired;

    [RelayCommand]
    void PairAgain() => shell.OpenSettingsPage();

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
        try { Fill(await client.Projects()); Unpaired = false; }
        catch (DargClientException e) { Bad = true; Status = e.Message; Unpaired = e.Unpaired; }
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
        new("Home", "⌂", "AND-0008", "what's going on in this project"),
        new("Board", "▦", "AND-0004", "this project's boards, one list at a time"),
        new("Agents", "◉", "AND-0005", "this project's agents and their transcripts"),
        new("Notes", "✎", "AND-0008", "the notes wall"),
        new("Docs", "▤", "AND-0008", "this project's docs"),
        new("Reports", "✓", "AND-0008", "finished agents' reports"),
    ];

    static readonly HashSet<string> Always = ["Home", "Agents", "Reports"];

    public ProjectInfo Project { get; }
    public IReadOnlyList<SectionTab> Tabs { get; }
    public override string Title => Project.Name;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Placeholder))] PhoneSection _section;

    public string Placeholder => $"Coming with {Section.Card}: {Section.What}.";

    readonly ShellViewModel? _shell;
    FettleChatViewModel? _fettle;
    AgentsListViewModel? _agents;

    /// <summary>The card or doc open in the section showing, which Fettle is told about: set by the section that opens
    /// one (AND-0004's card panel, AND-0008's doc reader).</summary>
    [ObservableProperty] string? _openCard;
    [ObservableProperty] string? _openDoc;

    /// <summary>What this page shows, as Fettle is told it with each message.</summary>
    public PhoneView Showing => new(Section.Name, OpenCard, OpenDoc);

    public bool CanTalkToFettle => _shell?.Client is not null;

    readonly Dictionary<string, ReadSection> _read = [];

    /// <summary>The picked section's own view model where its card has landed (Home, Notes, Docs and Reports:
    /// AND-0008); null shows the placeholder. Each is made on its first showing and kept, read again with ⟳.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasContent))] [NotifyPropertyChangedFor(nameof(ShowsPlaceholder))] object? _content;
    public bool HasContent => Content is not null;

    /// <summary>The placeholder shows for a section whose card has not landed: no content and not the board.</summary>
    public bool ShowsPlaceholder => !HasContent && !ShowsBoard;

    /// <summary>Fettle's button shows when connected, but not over the board's move sheet, whose buttons it would cover.</summary>
    public bool FettleShows => CanTalkToFettle && !(ShowsBoard && Board!.SheetUp);

    /// <summary>The Board section (AND-0004), when the project has a board and the phone is connected.</summary>
    public BoardSectionViewModel? Board { get; }

    /// <summary>Whether the board is what shows above the bar (else the section's placeholder).</summary>
    public bool ShowsBoard => Section.Name == "Board" && Board is not null;

    public ProjectPageViewModel(ProjectInfo project, ShellViewModel? shell = null)
    {
        Project = project;
        _shell = shell;
        if (shell?.Client is { } client && project.Sections.Contains("Board", StringComparer.OrdinalIgnoreCase))
        {
            Board = new BoardSectionViewModel((command, args) => client.Call(command, args), project.Id);
            // the card open on the board is the one Fettle is told this page shows
            Board.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(BoardSectionViewModel.Open) && ShowsBoard) OpenCard = Board.Open?.Card.Id;
                if (e.PropertyName == nameof(BoardSectionViewModel.SheetUp)) OnPropertyChanged(nameof(FettleShows));
            };
        }
        Tabs = All.Where(s => Always.Contains(s.Name) || project.Sections.Contains(s.Name, StringComparer.OrdinalIgnoreCase))
                  .Select(s => new SectionTab(s)).ToList();
        _section = Tabs[0].Section;
        Tabs[0].On = true;
        _content = ContentFor(_section);
        if (ShowsBoard) _ = Board!.Refresh();
    }

    public override bool HandleBack() => ShowsBoard && Board!.HandleBack();

    partial void OnSectionChanged(PhoneSection value)
    {
        foreach (var t in Tabs) t.On = t.Section == value;
        OnPropertyChanged(nameof(ShowsBoard));
        OnPropertyChanged(nameof(FettleShows));
        OnPropertyChanged(nameof(ShowsPlaceholder));
        if (Board is not null) OpenCard = ShowsBoard ? Board.Open?.Card.Id : null; // a card left open on the board isn't what another section shows
        if (ShowsBoard) _ = Board!.Refresh(); // each visit reads it again: the PC may have moved cards meanwhile
        Content = ContentFor(value);
    }

    object? ContentFor(PhoneSection section)
    {
        if (_shell is null) return null;
        if (section.Name == "Agents") return _agents ??= new AgentsListViewModel(_shell, Project.Id); // AND-0005
        if (!_read.TryGetValue(section.Name, out var read))
        {
            read = section.Name switch
            {
                "Home" => new HomeViewModel(_shell, Project.Id),
                "Notes" => new NotesViewModel(_shell, Project.Id),
                "Docs" => new DocsViewModel(_shell, Project.Id),
                "Reports" => new ReportsViewModel(_shell, Project.Id),
                _ => null!,
            };
            if (read is null) return null;
            read.AskFettle = FettleFrom;
            _read[section.Name] = read;
        }
        read.EnsureLoaded();
        return read;
    }

    [RelayCommand]
    void Pick(SectionTab tab) => Section = tab.Section;

    /// <summary>Opens Fettle full screen (AND-0007); the same chat each time for this page, so it carries on.</summary>
    [RelayCommand]
    public void Fettle()
    {
        OpenDoc = null; // from the page itself no doc is open; a doc page's own button sets it (FettleFrom)
        ShowFettle();
    }

    /// <summary>Fettle from a doc or report this page opened (AND-0008): told the doc that's open, if it's one.</summary>
    internal void FettleFrom(ReadPageViewModel page)
    {
        OpenDoc = page.DocPath;
        ShowFettle();
    }

    void ShowFettle()
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
