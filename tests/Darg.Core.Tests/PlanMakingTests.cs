using Darg.Core;
using Xunit;

namespace Darg.Core.Tests;

/// <summary>WOR-0201: Blocked by entries that name a heading with commas or "and" in it, or a card in a plan beside this
/// one, and making cards across plans in the order they unblock each other. The plans in PlanData/seamless are copies of
/// docs/design's Seamless machine switching plans as they were on 2026-11-25, with the cross-plan waits their Design
/// parts wrote as prose put into Blocked by lines (the new form): Put .darg waits on Keep both sides, Auto-sync's round
/// on One sync round, and the two settings-repo cards on Sync now and the unattended round.</summary>
public class PlanMakingTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "darg-planmaking-" + Guid.NewGuid().ToString("N")[..8]);
    string Design => Path.Combine(_root, "design");

    public PlanMakingTests() => Directory.CreateDirectory(Design);
    public void Dispose() => Directory.Delete(_root, true);

    public static string Seamless()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var f = Path.Combine(d.FullName, "tests", "Darg.Core.Tests", "PlanData", "seamless");
            if (Directory.Exists(f)) return f;
        }
        throw new DirectoryNotFoundException("tests/Darg.Core.Tests/PlanData/seamless");
    }

    string Copy(params string[] names)
    {
        foreach (var n in names.Length > 0 ? names : Directory.GetFiles(Seamless(), "*.md").Select(Path.GetFileNameWithoutExtension).ToArray())
            File.Copy(Path.Combine(Seamless(), n + ".md"), Path.Combine(Design, n + ".md"));
        return Path.Combine(Design, (names.Length > 0 ? names[0] : "seamless-machine-switching") + ".md");
    }

    string Save(string name, string text)
    {
        var f = Path.Combine(Design, name + ".md");
        File.WriteAllText(f, text.ReplaceLineEndings("\n"));
        return f;
    }

    string Boards()
    {
        var boards = Path.Combine(_root, "boards");
        BoardStore.CreateBoard(boards, "Main", ["To Do", "In Progress", "QA", "Done"], "WOR");
        return boards;
    }

    static string Card(string title, string blockedBy = "none", bool thin = false) => $"""

        ### {title}
        A card for the test, with enough words in it to say what is wanted and why it matters to the person reading it cold.
        **Where:** `src/{title.Length}.cs`
        {(thin ? "" : "**Done when:** a test shows it working.")}
        **Blocked by:** {blockedBy}

        """;

    [Fact]
    public void A_blocked_by_naming_a_heading_with_commas_or_and_is_one_entry()
    {
        var spike = PlanDoc.Load(Copy("resume-spike"));
        Assert.Equal([[], ["Spike script: make a session, copy it under another path, resume it"], ["Run the spike on this PC and record what has to be copied"],
            ["Run the Mac step and write what the agent makes of Windows paths"]], spike.Cards.Select(c => c.BlockedBy.ToArray()));
        Assert.Empty(spike.UnknownBlockers());
        Assert.Same(spike.Cards[0], spike.Find(spike.Cards[1].BlockedBy[0]));

        // two headings with " and " and backticks in one line, beside a plain one
        var leaving = PlanDoc.Load(Copy("leaving-check"));
        Assert.Equal(["The Leaving check panel and button", "Leaving check for `.darg`"], leaving.Cards.Single(c => c.Title == "Check on close").BlockedBy);
        Assert.Empty(leaving.UnknownBlockers());

        // what names nothing is still cut where it always was; none is nothing; an id stays an id
        Assert.Equal(["A", "B", "C", "AND-0003"], PlanScope.Split("A, B and C; AND-0003.", _ => false));
        Assert.Empty(PlanScope.Split("none", _ => false));
        Assert.Equal(["x, y and z", "w"], PlanScope.Split("**x, y and z**, w", s => PlanScope.Clean(s) == "x, y and z"));
    }

    [Fact]
    public void Make_cards_on_the_resume_spike_plan_makes_all_four_each_blocked_by_the_one_before()
    {
        var f = Copy("resume-spike");
        var boards = Boards();
        var first = PlanStore.MakeCards(f, boards);
        Assert.True(first.NeedsConfirm, first.Problem);   // three questions are open
        Assert.Empty(first.Cant);
        Assert.Equal(4, first.CanMake);
        var r = PlanStore.MakeCards(f, boards, confirmOpen: true);
        Assert.True(r.Ok, r.Problem);
        var plan = PlanDoc.Load(f);
        Assert.Equal(plan.Cards.Select(c => c.Title), r.Made.Select(m => m.Title));   // the file's order is the Blocked by order here
        var cards = BoardStore.ReadCards(Path.Combine(boards, "Resume spike")).ToDictionary(c => c.Id);
        Assert.Equal(4, cards.Count);
        Assert.Empty(cards[r.Made[0].Id].BlockedBy);
        for (var i = 1; i < 4; i++) Assert.Equal([r.Made[i - 1].Id], cards[r.Made[i].Id].BlockedBy);
        Assert.All(plan.Cards, c => Assert.True(c.Made));
    }

    [Fact]
    public void A_heading_in_a_sibling_plan_resolves_there_and_a_clash_or_nothing_is_flagged()
    {
        Save("proj", "---\nkind: project\n---\n# Proj\n\n## Features\n- [Alpha](a.md): one\n- [Beta](b.md): two\n- [Gamma](c.md): three\n");
        Save("a", "---\nkind: feature\n---\n# Alpha\n\n## Cards\n" + Card("Shared") + Card("Make it, test it and ship it"));
        Save("b", "---\nkind: feature\nparent: proj\n---\n# Beta\n\n## Cards\n" + Card("Shared") + Card("Beta work", "Shared, Make it, test it and ship it"));
        Save("c", "---\nkind: feature\n---\n# Gamma\n\n## Cards\n" + Card("Gamma work", "Shared, Alpha: Shared, Nope, AND-0009"));
        Save("other", "---\nkind: feature\n---\n# Other\n\n## Cards\n" + Card("Elsewhere", "Make it, test it and ship it"));
        var plans = PlanStore.List(Design);
        PlanDoc P(string slug) => plans.Single(p => p.Slug == slug);

        var b = P("b");
        var beta = PlanScope.For(b, plans).BlockersOf(b, b.Cards[1]);
        Assert.Equal([("Shared", "b"), ("Make it, test it and ship it", "a")], beta.Select(x => (x.Card!.Title, x.Plan!.Slug)));   // its own plan first

        var c = P("c");
        var gamma = PlanScope.For(c, plans).BlockersOf(c, c.Cards[0]);
        Assert.Equal(4, gamma.Count);
        Assert.Contains("a card in Alpha and in Beta", gamma[0].Problem);
        Assert.Contains("\"Alpha: Shared\"", gamma[0].Problem);   // says how to write it
        Assert.Equal("a", gamma[1].Plan!.Slug);
        Assert.Contains("no card in this plan or the plans beside it", gamma[2].Problem);
        Assert.True(gamma[3].External);

        // a plan under another project (or none) can't see these
        var other = P("other");
        Assert.False(PlanScope.For(other, plans).BlockersOf(other, other.Cards[0])[0].Found);
    }

    [Fact]
    public void Rows_that_cant_be_made_are_named_and_the_rest_made_when_asked()
    {
        Save("proj", "---\nkind: project\n---\n# Proj\n\n## Features\n- [Alpha](a.md): one\n- [Beta](b.md): two\n");
        Save("a", "---\nkind: feature\n---\n# Alpha\n\n## Cards\n" + Card("Alpha base"));
        var bf = Save("b", "---\nkind: feature\n---\n# Beta\n\n## Cards\n" + Card("Needs alpha", "Alpha base") + Card("Thin one", thin: true)
            + Card("Typo", "Nothing like it") + Card("After thin", "Thin one") + Card("Free") + Card("After free", "Free"));
        var boards = Boards();
        var r = PlanStore.MakeCards(bf, boards);
        Assert.False(r.Ok);
        Assert.Empty(r.Made);
        Assert.Equal(2, r.CanMake);
        var why = r.Cant.ToDictionary(c => c.Card, c => c.Reason);
        Assert.Equal(["Needs alpha", "Thin one", "Typo", "After thin"], why.Keys);
        Assert.Contains("waits on \"Alpha base\" in Alpha, which has no id yet", why["Needs alpha"]);
        Assert.Contains("2/3", why["Thin one"]);
        Assert.Contains("\"Nothing like it\" is no card", why["Typo"]);
        Assert.Contains("waits on \"Thin one\", which can't be made", why["After thin"]);
        Assert.Contains("4 of the 6 cards can't be made", r.Problem);
        Assert.False(Directory.Exists(Path.Combine(boards, "Beta")));

        var rest = PlanStore.MakeCards(bf, boards, skipBlocked: true);
        Assert.True(rest.Ok, rest.Problem);
        Assert.Equal(["Free", "After free"], rest.Made.Select(m => m.Title));
        Assert.Equal(4, rest.Cant.Count);

        // once Alpha's card is made, Beta's waits on it by id
        Assert.True(PlanStore.MakeCards(Path.Combine(Design, "a.md"), boards).Ok);
        var again = PlanStore.MakeCards(bf, boards, ["Needs alpha"]);
        Assert.True(again.Ok, again.Problem);
        var alphaId = PlanDoc.Load(Path.Combine(Design, "a.md")).Cards[0].Id;
        Assert.Equal([alphaId], BoardStore.ReadCards(Path.Combine(boards, "Beta")).Single(c => c.Title == "Needs alpha").BlockedBy);
    }

    [Fact]
    public void Make_all_on_the_seamless_plans_makes_every_features_cards_on_its_board_cross_plan_blockers_first()
    {
        var project = Copy();
        var boards = Boards();
        var first = PlanStore.MakeAll(project, boards);
        Assert.True(first.NeedsConfirm, first.Problem + string.Join("\n", first.Cant.Select(c => c.Text)));
        Assert.Empty(first.Cant);
        var r = PlanStore.MakeAll(project, boards, confirmOpen: true);
        Assert.True(r.Ok, r.Problem);

        var plans = PlanStore.List(Design);
        var features = plans.Where(p => !p.IsProject).ToList();
        Assert.Equal(8, features.Count);
        Assert.Equal(features.Sum(p => p.Cards.Count), r.Made.Count);
        Assert.All(features, p => Assert.All(p.Cards, c => Assert.True(c.Made, $"{p.Title}: {c.Title}")));
        Assert.All(features, p => Assert.Equal("agreed", p.Status));

        // each feature's cards on its own board, named for the plan
        var order = r.Detail.Select((m, i) => (m.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var all = new Dictionary<string, Card>();
        foreach (var p in features)
        {
            Assert.Equal(p.Title.TrimStart('.'), p.Board);   // a board's name never starts with a dot
            var onBoard = BoardStore.ReadCards(Path.Combine(boards, p.Board)).ToDictionary(c => c.Id);
            Assert.Equal(p.Cards.Select(c => c.Id).Order(), onBoard.Keys.Order());
            foreach (var (id, card) in onBoard) all[id] = card;
        }
        // every Blocked by holds real ids, each made before the card that waits on it, as many as the plan names
        foreach (var p in features)
            foreach (var pc in p.Cards)
            {
                var card = all[pc.Id];
                Assert.Equal(PlanScope.For(p, plans).BlockersOf(p, pc).Count, card.BlockedBy.Count);
                Assert.All(card.BlockedBy, b => Assert.True(all.ContainsKey(b) && order[b] < order[pc.Id], $"{pc.Title} waits on {b}"));
            }
        string Id(string title) => features.SelectMany(p => p.Cards).Single(c => c.Title == title).Id;
        Assert.Contains(Id("Keep both sides of a sync conflict"), all[Id("Put .darg on a darg branch and bring today's files in")].BlockedBy);
        Assert.Contains(Id("One sync round for the darg branch"), all[Id("A sync round that runs unattended")].BlockedBy);
        Assert.Contains(Id("Sync now in the footer and the API"), all[Id("Connect the settings repo from Settings")].BlockedBy);

        // again: nothing left
        Assert.Contains("already", PlanStore.MakeAll(project, boards, confirmOpen: true).Problem);
    }

    [Fact]
    public void A_loop_across_plans_makes_nothing_and_names_it()
    {
        var project = Copy();
        var auto = Path.Combine(Design, "auto-sync.md");
        var text = File.ReadAllText(auto);
        var at = text.IndexOf("**Blocked by:** none", text.IndexOf("### Keep both sides of a sync conflict"));
        File.WriteAllText(auto, text[..at] + "**Blocked by:** Put .darg on a darg branch and bring today's files in" + text[(at + "**Blocked by:** none".Length)..]);
        var boards = Boards();
        var r = PlanStore.MakeAll(project, boards, confirmOpen: true);
        Assert.False(r.Ok);
        Assert.Empty(r.Made);
        var loop = r.Cant.Single(c => c.Card == "Keep both sides of a sync conflict").Reason;
        Assert.Contains("loop", loop);
        Assert.Contains("\"Put .darg on a darg branch and bring today's files in\" (.darg on its own branch)", loop);
        Assert.Contains("\"Keep both sides of a sync conflict\" (Auto-sync)", loop);
        Assert.Contains("loop", r.Problem);
        Assert.Equal(["Main"], BoardStore.List(boards));
    }
}
