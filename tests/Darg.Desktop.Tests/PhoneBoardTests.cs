using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;
using Darg.Mobile;
using Darg.Mobile.ViewModels;
using Darg.Mobile.Views;

namespace Darg.Desktop.Tests;

/// <summary>AND-0004: the phone's board, run headless against the real desktop app's API over HTTP (as the phone reaches
/// it): triage a backlog by reading cards, moving them with the move sheet and adding one with the quick-add.</summary>
public class PhoneBoardTests
{
    [AvaloniaFact]
    public async Task A_backlog_is_triaged_from_the_phone()
    {
        using var app = DargApp.Make();
        var first = app.Card("Paint the splash screen", description: "Teal, with Fettle waving.");
        var second = app.Card("The export button crashes", type: "Bug");
        var third = app.Card("Tidy the docs index");
        var waiting = app.Card("Check the header", "QA Ready");
        BoardStore.SetBlockedBy(app.BoardFolder, third.Id, [first.Id]);
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        using var client = Client();
        var shell = new ShellViewModel(Store(app, client.Connection), _ => client);
        var window = Phone(shell);
        var project = shell.OpenProjects();
        await DargApp.Wait(() => project.Projects.Count == 1, "the projects");
        shell.Show(new ProjectPageViewModel(project.Projects[0], shell));
        var page = Assert.IsType<ProjectPageViewModel>(shell.Page);
        var board = Assert.IsType<BoardSectionViewModel>(page.Board);
        Assert.False(page.ShowsBoard); // the page opens to Home (AND-0008)
        Assert.Empty(board.Lists); // and the board isn't read until it's picked
        page.PickCommand.Execute(page.Tabs.Single(t => t.Section.Name == "Board"));
        Assert.True(page.ShowsBoard);
        Assert.False(page.ShowsPlaceholder);
        await DargApp.Wait(() => board.Lists.Count > 0, "the board to load on the phone");

        // the backlog shows first, every list is a page (empty ones too), and the strip counts them
        Assert.Equal("Backlog", board.Current?.Name);
        Assert.Equal(new[] { "Backlog" }.Concat(DargApp.Columns).Append("Completed"), board.Lists.Select(l => l.Name));
        Assert.Equal([first.Id, second.Id, third.Id], board.Current!.Cards.Select(c => c.Id));
        Assert.True(board.Current.Cards[1].IsBug);
        Assert.Equal($"waits on {first.Id}", board.Current.Cards[2].Blockers); // the desktop's blocker chip, on the phone too
        Assert.False(board.Current.Cards[0].HasBlockers);
        Assert.Equal("QA Ready 1", board.Lists.Single(l => l.Kind == "qa").Label);
        Shot(window, app, "1-backlog");

        // a tap reads the card
        await board.OpenCard(board.Current.Cards[0]);
        Assert.Equal($"{first.Id}: Paint the splash screen", board.Open?.Heading);
        Assert.Equal("Teal, with Fettle waving.", board.Open?.Body);
        Assert.Equal(first.Id, page.Showing.Card); // Fettle (AND-0007) is told the card that's open
        Shot(window, app, "2-card");
        Assert.True(shell.Back()); // Android's back closes the card, not the project
        Assert.Null(board.Open);
        Assert.Null(page.Showing.Card);
        Assert.Same(page, shell.Page);

        // the move sheet offers the board's other lists; picking one moves it on disk and on the desktop's board
        var tile = board.Current.Cards.Single(c => c.Id == second.Id);
        board.StartMove(tile);
        Assert.True(board.SheetUp);
        Assert.DoesNotContain(board.MoveTargets, l => l.Name == "Backlog");
        Assert.False(page.FettleShows); // Fettle's button would cover the sheet's buttons
        await board.OpenCard(tile); // the tap that ends a long press finds the sheet up and opens nothing
        Assert.Null(board.Open);
        Shot(window, app, "3-move-sheet");
        await board.MoveTo(board.MoveTargets.Single(l => l.Name == "In Progress"));
        Assert.False(board.SheetUp);
        Assert.True(page.FettleShows);
        Assert.Equal("In Progress", app.Read(second.Id).Status);
        Assert.Equal("Backlog", board.Current?.Name); // the phone stays on the list it was on
        Assert.Equal([first.Id, third.Id], board.Current!.Cards.Select(c => c.Id));
        Assert.Equal("In Progress 1", board.Lists.Single(l => l.Name == "In Progress").Label);
        Assert.Equal([second.Id], app.Board.Lists.Single(l => l.Name == "In Progress").Cards.Select(c => c.Id));
        Assert.Contains("Moved", board.Status);

        // swiping on to To Do and typing a title adds the card there
        board.ListIndex = board.Lists.ToList().FindIndex(l => l.Name == "To Do");
        board.QuickTitle = "Sort the labels";
        Assert.True(board.QuickAddCommand.CanExecute(null));
        await board.QuickAdd();
        var added = BoardStore.ReadCards(app.BoardFolder).Single(c => c.Title == "Sort the labels");
        Assert.Equal("To Do", added.Status);
        Assert.Equal([added.Id], board.Current!.Cards.Select(c => c.Id));
        Assert.Equal("", board.QuickTitle);
        Shot(window, app, "4-added");

        // swiping on to a list whose name is off the strip's edge scrolls the strip to it
        var strip = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "Strip");
        Assert.Equal(0, strip.Offset.X);
        board.ListIndex = board.Lists.Count - 1;
        await DargApp.Settle();
        Assert.True(strip.Offset.X > 0, "the strip didn't follow the swipe to Completed");
        Shot(window, app, "5-last-list");

        // the QA card was left alone throughout
        Assert.Equal("QA Ready", app.Read(waiting.Id).Status);
        window.Close();
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task A_move_the_desktop_refuses_says_why_and_the_phone_keeps_what_it_had()
    {
        using var app = DargApp.Make();
        var card = app.Card("Paint the splash screen");
        await app.Start();
        using var client = Client();
        var board = new BoardSectionViewModel((c, a) => client.Call(c, a), "proj");
        await board.Refresh();
        Assert.Equal([card.Id], board.Current!.Cards.Select(c => c.Id));

        BoardStore.Delete(app.BoardFolder, card.Id); // gone on the PC since the phone last looked
        board.StartMove(board.Current.Cards[0]);
        await board.MoveTo(board.MoveTargets.First(l => l.Name == "To Do"));
        Assert.True(board.Bad);
        Assert.Contains($"No card {card.Id}", board.Status);

        await board.Refresh();
        Assert.Empty(board.Current!.Cards);
        Assert.False(board.Bad); // a fresh read clears the error
    }

    [AvaloniaFact]
    public async Task A_tap_on_a_tile_opens_it_and_a_long_press_puts_up_its_move_sheet()
    {
        using var app = DargApp.Make();
        var card = app.Card("Paint the splash screen", description: "Teal.");
        await app.Start();
        using var client = Client();
        var shell = new ShellViewModel(Store(app, client.Connection), _ => client);
        var window = Phone(shell);
        var projects = shell.OpenProjects();
        await DargApp.Wait(() => projects.Projects.Count == 1, "the projects");
        shell.Show(new ProjectPageViewModel(projects.Projects[0], shell));
        var page = (ProjectPageViewModel)shell.Page!;
        page.PickCommand.Execute(page.Tabs.Single(t => t.Section.Name == "Board"));
        var board = page.Board!;
        await DargApp.Wait(() => board.Current?.Cards.Count == 1, "the backlog on the phone");
        await DargApp.Settle();
        Button Tile() => window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("tile") && b.IsEffectivelyVisible);

        // a tap (press and release at once) opens the card through the Carousel's swipe handling
        var at = Centre(Tile());
        HeadlessWindowExtensions.MouseDown(window, at, Avalonia.Input.MouseButton.Left);
        HeadlessWindowExtensions.MouseUp(window, at, Avalonia.Input.MouseButton.Left);
        await DargApp.Wait(() => board.Open?.Body == "Teal.", "the tapped card to open");
        Assert.False(board.SheetUp);
        Assert.True(shell.Back());

        // a press held past the hold time puts up the sheet; the release that ends it opens nothing. Avalonia starts a hold
        // only on an odd click count, and a press on the same spot within the double-tap time of the tap above counts as
        // its second click, so wait that out first: with a warm app the tap and back took under 500 ms and the hold never came
        // (DefaultPlatformSettings.GetDoubleTapTime: 500 ms, which the headless platform keeps)
        await DargApp.Settle(600);
        at = Centre(Tile());
        HeadlessWindowExtensions.MouseDown(window, at, Avalonia.Input.MouseButton.Left);
        await DargApp.Wait(() => board.SheetUp, "the long press to put up the move sheet", seconds: 5);
        HeadlessWindowExtensions.MouseUp(window, at, Avalonia.Input.MouseButton.Left);
        await DargApp.Settle(200);
        Assert.Equal(card.Id, board.Moving?.Id);
        Assert.Null(board.Open);
        Shot(window, app, "held");
        window.Close();
    }

    static Avalonia.Point Centre(Avalonia.Visual c) =>
        Avalonia.VisualExtensions.TranslatePoint(c, new Avalonia.Point(c.Bounds.Width / 2, c.Bounds.Height / 2), TopLevel.GetTopLevel(c)!)!.Value;

    [Fact]
    public void A_cards_body_is_its_description_and_sections_without_the_header_lines()
    {
        var text = "# T-0001: Paint it\r\n\r\n- **Status:** Backlog\r\n- **Labels:** art\r\n\r\n## Description\r\n\r\nTeal.\r\n\r\n## How to check\r\n\r\nLook.\r\n";
        Assert.Equal("Teal.\n\n## How to check\n\nLook.", PhoneCardDetail.BodyOf(text));
        Assert.Equal("(no description)", PhoneCardDetail.BodyOf("# T-0001: x\n\n- **Status:** Backlog\n\n## Description\n"));
    }

    static DargClient Client()
    {
        var parsed = Connection.ParseAddress(ArtifactHost.Server!.ApiUrl)!.Value;
        return new DargClient(new Connection(parsed.Address, parsed.Token!));
    }

    static ConnectionStore Store(DargApp app, Connection c)
    {
        var store = new ConnectionStore(Path.Combine(app.Folder, "phone", "connection.json"));
        store.Save(c);
        return store;
    }

    /// <summary>The phone's shell in a phone-sized window, so the shots look as the phone would.</summary>
    static Window Phone(ShellViewModel shell)
    {
        var window = new Window { Width = 412, Height = 860, Content = new ShellView { DataContext = shell } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    static void Shot(Window window, DargApp app, string name)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nothing was drawn.");
#pragma warning disable CS0618
        frame.Save(Path.Combine(app.Folder, "phone-board-" + name + ".png"));
#pragma warning restore CS0618
    }
}
