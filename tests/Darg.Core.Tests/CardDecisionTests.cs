using Darg.Core;
using Xunit;

namespace Darg.Core.Tests;

/// <summary>WOR-0124: a card in For Human Attention carries the decision it waits on, as a ## Decision section.</summary>
public class CardDecisionTests
{
    static readonly Card C = new("WOR-0200", "Voice mode", "For Human Attention", "", "Task", [], "WOR-0200 - Voice mode.md", "Dictate into the box.");

    const string Section =
        "## Decision\n\n" +
        "**Question:** Does dictation work for you in a build from 5b85baa?\n" +
        "**Asked by:** agent 0fa5c0cd01, 2026-11-18 00:00\n\n" +
        "- **Works** (recommended) → done: words land in the box.\n" +
        "- **Still fails** → back: say what the box showed.\n" +
        "- **Not tried yet** → stay: leave it here.\n" +
        "- **Use Whisper instead**: the agent swaps the engine.\n\n" +
        "**Context:** builds and tests pass;\na real microphone can't be tried from an agent.\n";

    static string CardText(string extra = "") =>
        "# WOR-0200: Voice mode\n\n- **Status:** For Human Attention\n\n## Description\n\nDictate into the box.\n\n" + extra + "## How to check\n\nClick the mic.\n";

    [Fact]
    public void The_section_reads_as_a_question_with_options_actions_and_a_recommendation()
    {
        var d = CardDecision.Read(CardText(Section + "\n"))!;
        Assert.True(d.Valid);
        Assert.Equal("Does dictation work for you in a build from 5b85baa?", d.Question);
        Assert.Equal("0fa5c0cd01", d.AgentId);
        Assert.False(d.Drafted);
        Assert.Equal(["Works", "Still fails", "Not tried yet", "Use Whisper instead"], d.Options.Select(o => o.Label));
        Assert.Equal([DecisionAction.Done, DecisionAction.Back, DecisionAction.Stay, DecisionAction.Agent], d.Options.Select(o => o.Action));
        Assert.Equal([true, false, false, false], d.Options.Select(o => o.Recommended));
        Assert.Equal("words land in the box.", d.Options[0].Description);
        Assert.Equal("the agent swaps the engine.", d.Options[3].Description);
        Assert.Equal("builds and tests pass;\na real microphone can't be tried from an agent.", d.Context);
        Assert.Equal(DecisionAction.Back, d.OptionFor("still FAILS")!.Action);
        Assert.Null(d.OptionFor("something else"));
    }

    // WOR-0186: an option can send the card to any column, by name
    [Theory]
    [InlineData("- **Re-check** → move to QA: look again", DecisionAction.Move, "QA", "look again")]
    [InlineData("- **Re-check** (recommended) → QA: look again", DecisionAction.Move, "QA", "look again")]
    [InlineData("- **Park it** → backlog", DecisionAction.Move, "backlog", "")]
    [InlineData("- **Carry on** -> move to In Progress - keep going", DecisionAction.Move, "In Progress", "keep going")]
    [InlineData("- **Rework** → Needs More Work: fix it", DecisionAction.Move, "Needs More Work", "fix it")]
    [InlineData("- **Ask** → agent: it decides", DecisionAction.Agent, null, "it decides")]
    public void An_option_can_name_any_column_to_move_to(string line, DecisionAction action, string? target, string description)
    {
        var d = CardDecision.Parse("**Question:** Next?\n" + line + "\n- **Other** → done");
        var o = d.Options[0];
        Assert.Equal(action, o.Action);
        Assert.Equal(target, o.Target);
        Assert.Equal(description, o.Description);
        Assert.Equal(DecisionAction.Done, d.Options[1].Action);
        var again = CardDecision.Parse(d.Format()).Options[0];
        Assert.Equal((o.Action, o.Target), (again.Action, again.Target));
    }

    [Fact]
    public void Targets_resolve_to_board_columns_by_name_or_role()
    {
        IReadOnlyList<Column> cols = [new("c1", "To Do"), new("c2", "In Progress"), new("c3", "QA Ready"), new("c4", "For Human Attention"), new("c5", "needs more work"), new("c6", "Done")];
        Assert.Equal("backlog", BoardColumns.Resolve(cols, "Backlog"));
        Assert.Equal("c2", BoardColumns.Resolve(cols, "in  progress"));
        Assert.Equal("c3", BoardColumns.Resolve(cols, "QA"));
        Assert.Equal("c5", BoardColumns.Resolve(cols, "rework"));
        Assert.Equal("c6", BoardColumns.Resolve(cols, "done"));
        Assert.Null(BoardColumns.Resolve(cols, "Shipping"));
    }

    // the shapes agents drift into when writing an option line by hand
    [Theory]
    [InlineData("- **Works (Recommended)** → done: words land", "Works", true, DecisionAction.Done, "words land")]
    [InlineData("- **Works** (recommended) -> done - words land", "Works", true, DecisionAction.Done, "words land")]
    [InlineData("* Works (recommended) => approve: words land", "Works", true, DecisionAction.Done, "words land")]
    [InlineData("1. **Still fails** → send back — say what you saw", "Still fails", false, DecisionAction.Back, "say what you saw")]
    [InlineData("- Not yet → wait", "Not yet", false, DecisionAction.Stay, "")]
    [InlineData("- **Keep both**: the agent keeps both engines", "Keep both", false, DecisionAction.Agent, "the agent keeps both engines")]
    [InlineData("- [ ] **Works** — recommended → done: fine", "Works", true, DecisionAction.Done, "fine")]
    public void Option_lines_are_read_whatever_shape_they_come_in(string line, string label, bool recommended, DecisionAction action, string description)
    {
        var d = CardDecision.Parse("Question: Which?\n" + line + "\n- **Other** → stay\n");
        var o = d.Options[0];
        Assert.Equal(label, o.Label);
        Assert.Equal(recommended, o.Recommended);
        Assert.Equal(action, o.Action);
        Assert.Equal(description, o.Description);
    }

    [Fact]
    public void No_section_or_too_few_options_is_no_decision()
    {
        Assert.Null(CardDecision.Read(CardText()));
        Assert.False(CardDecision.Read(CardText("## Decision\n\n**Question:** Ship it?\n\n- **Yes** → done\n\n"))!.Valid);
        Assert.False(CardDecision.Read(CardText("## Decision\n\n- **Yes** → done\n- **No** → back\n\n"))!.Valid);
        // a question as a bare line still counts
        Assert.True(CardDecision.Read(CardText("## Decision\n\nShip it?\n\n- **Yes** → done\n- **No** → back\n\n"))!.Valid);
    }

    [Fact]
    public void Writing_puts_it_below_the_description_and_replaces_an_open_one()
    {
        var file = Path.Combine(Directory.CreateTempSubdirectory("decision").FullName, "card.md");
        File.WriteAllText(file, CardText());
        var d = CardDecision.Parse(Section.Replace("## Decision\n\n", ""));
        d.WriteTo(file);
        var text = File.ReadAllText(file);
        Assert.True(text.IndexOf("## Description") < text.IndexOf("## Decision") && text.IndexOf("## Decision") < text.IndexOf("## How to check"), text);
        Assert.Equal(d.Question, CardDecision.Read(text)!.Question);
        Assert.Equal(4, CardDecision.Read(text)!.Options.Count);

        (d with { Question = "Ship it?" }).WriteTo(file);
        text = File.ReadAllText(file);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "## Decision"));
        Assert.Equal("Ship it?", CardDecision.Read(text)!.Question);
        Assert.Contains("## How to check\n\nClick the mic.", text);
    }

    /// <summary>WOR-0124 QA: reading took any case and answering only "## Decision", so a "## decision" card was shown and
    /// moved but its answer never written. Whatever heading Read takes, Answer and WriteTo must take too.</summary>
    [Theory]
    [InlineData("## decision")]
    [InlineData("## DECISION")]
    [InlineData("##   Decision  ")]
    [InlineData("##\tdecision")]
    public void Any_heading_read_as_a_decision_is_answered_and_replaced_too(string heading)
    {
        foreach (var crlf in new[] { false, true })
        {
            var file = Path.Combine(Directory.CreateTempSubdirectory("decision").FullName, "card.md");
            var card = CardText(Section.Replace("## Decision", heading) + "\n");
            File.WriteAllText(file, crlf ? card.Replace("\n", "\r\n") : card);
            Assert.True(CardDecision.Read(File.ReadAllText(file))!.Valid, heading);

            CardDecision.Parse("Question: Ship it?\n- **Yes** → done\n- **No** → back\n").WriteTo(file);
            var text = File.ReadAllText(file);
            Assert.Equal("Ship it?", CardDecision.Read(text)!.Question);
            Assert.DoesNotContain("in a build from 5b85baa", text); // replaced, not a second section

            Assert.Equal("Ship it?", CardDecision.Answer(file, "Yes", "", new DateTime(2026, 11, 18, 9, 12, 0))!.Question);
            text = File.ReadAllText(file);
            Assert.Null(CardDecision.Read(text));
            Assert.Contains("**Answer:** Yes", text);
        }
    }

    [Fact]
    public void Writing_a_card_with_no_description_heading_appends_it()
    {
        var file = Path.Combine(Directory.CreateTempSubdirectory("decision").FullName, "card.md");
        File.WriteAllText(file, "# WOR-0200: Voice mode\r\n\r\n- **Status:** For Human Attention\r\n");
        CardDecision.Parse(Section).WriteTo(file);
        var text = File.ReadAllText(file);
        Assert.Contains("\r\n## Decision\r\n", text); // a CRLF card stays CRLF
        Assert.True(CardDecision.Read(text)!.Valid);
    }

    [Fact]
    public void Answering_records_the_answer_and_closes_the_decision()
    {
        var file = Path.Combine(Directory.CreateTempSubdirectory("decision").FullName, "card.md");
        File.WriteAllText(file, CardText(Section + "\n"));
        var when = new DateTime(2026, 11, 18, 9, 12, 0);
        var answered = CardDecision.Answer(file, "Still fails", "the box said no sound", when);
        Assert.Equal("Does dictation work for you in a build from 5b85baa?", answered!.Question);
        var text = File.ReadAllText(file);
        Assert.Null(CardDecision.Read(text)); // no open decision left
        Assert.Contains("## Decision answered 2026-11-18 09:12", text);
        Assert.Contains("**Answer:** Still fails (2026-11-18 09:12)\n**Notes:** the box said no sound\n\n## How to check", text);
        Assert.Null(CardDecision.Answer(file, "Works", "", when)); // nothing open to answer

        // the next round gets a fresh one, and the answered one stays as the record
        CardDecision.Parse("Question: Now?\n- **Yes** → done\n- **No** → back\n").WriteTo(file);
        text = File.ReadAllText(file);
        Assert.Equal("Now?", CardDecision.Read(text)!.Question);
        Assert.Contains("## Decision answered 2026-11-18 09:12", text);
    }

    [Fact]
    public void A_draft_is_taken_only_in_the_shape_asked_for()
    {
        var when = new DateTime(2026, 11, 18, 9, 0, 0);
        var good = CardDecision.ParseDraft("Question: Is it right as it stands?\n- Approve (recommended) → done: it works\n- Send back → back: something's off\nContext: tests pass; nobody tried it live.", when)!;
        Assert.True(good.Drafted);
        Assert.Null(good.AgentId);
        Assert.Equal(2, good.Options.Count);
        Assert.Equal("tests pass; nobody tried it live.", good.Context);

        Assert.Null(CardDecision.ParseDraft(null, when));
        Assert.Null(CardDecision.ParseDraft("Standard | ordinary work", when));
        Assert.Null(CardDecision.ParseDraft("Question: Is it right?\n- Approve → done", when)); // one option
        Assert.Null(CardDecision.ParseDraft("Question: Is it right\n- Approve → done\n- Back → back", when)); // no question mark
        Assert.Null(CardDecision.ParseDraft("Sure! Here's a decision:\nQuestion: Is it right?\n- Approve → done\n- Back → back", when)); // chatter
        Assert.Null(CardDecision.ParseDraft("Question: Pick?\n- A → done\n- B → back\n- C → stay\n- D\n- E", when)); // too many
        // two marked recommended: the first keeps it
        var two = CardDecision.ParseDraft("Question: Pick?\n- A (recommended) → done\n- B (recommended) → back", when)!;
        Assert.Equal([true, false], two.Options.Select(o => o.Recommended));
    }

    [Fact]
    public void What_agents_are_told_is_safe_to_pass_and_names_the_shape()
    {
        var rules = CardDecision.Rules("For Human Attention");
        Assert.Contains("## Decision", rules);
        Assert.Contains("(recommended)", rules);
        Assert.Contains("screenshot", rules);
        Assert.DoesNotContain("\"", rules);
        Assert.DoesNotContain("<", rules);
        Assert.DoesNotContain(">", rules);
        Assert.DoesNotContain("&", rules);
        Assert.DoesNotContain("|", rules);
        // the agent's rules carry it wherever the attention column is mentioned
        Assert.Contains("## Decision", ReportStore.Rules("C:/r", C, "QA Ready", "abc123", "For Human Attention"));
        Assert.DoesNotContain("## Decision", ReportStore.Rules("C:/r", C, "QA Ready", "abc123"));
        Assert.Contains("## Decision", ReportStore.QaRunPrompt("P", C, "f.md", "Done", "For Human Attention", "needs more work"));
    }

    [Fact]
    public void Why_here_is_the_newest_reason_else_how_to_check()
    {
        Assert.Equal("Click the mic.", CardDecision.WhyHere(CardText()));
        var text = CardText() + "\n## Why it needs attention\n\n### 2026-11-17 10:00\n\nfirst reason\n\n### 2026-11-18 10:00\n\nthe mic can't be tried\n";
        Assert.Equal("the mic can't be tried", CardDecision.WhyHere(text));
    }

    [Fact]
    public void The_decision_report_leads_with_the_cards_question()
    {
        var text = DecisionReport.Build(C, CardText(Section + "\n"), [], "", null, new DateTime(2026, 11, 18, 9, 0, 0));
        var decision = DecisionReport.Section(text, "Decision");
        Assert.StartsWith("**Question:** Does dictation work", decision);
        Assert.Contains("- **Works** (recommended) → done: words land in the box.", decision);
        Assert.Contains("- [ ] Approve", decision); // Reports' Approve and Send back still settle it
        Assert.DoesNotContain("**Recommendation:**", decision);
    }
}
