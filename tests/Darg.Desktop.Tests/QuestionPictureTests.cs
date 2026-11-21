using Avalonia.Headless.XUnit;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0184: an agent's question that points at a screenshot shows the picture above it.</summary>
public class QuestionPictureTests
{
    static async Task<QuestionItem> Ask(DargApp app, string text)
    {
        await app.Api("show", new { project = "proj", section = "Agents" });
        app.Agents.NewPrompt = text;
        app.Agents.StartAgentCommand.Execute(null);
        await app.Until(() => app.Agents.Selected?.Items.OfType<QuestionItem>().Any() == true, "the agent's question");
        return app.Agents.Selected!.Items.OfType<QuestionItem>().Single();
    }

    [AvaloniaFact]
    public async Task A_question_naming_a_screenshot_shows_it_above_the_question()
    {
        using var app = DargApp.Make();
        var png = Path.Combine(app.BoardFolder, "look.png");
        File.WriteAllBytes(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
        await app.Start();

        var q = await Ask(app, "FAKE_ASK FAKE_ASK_IMAGE " + png.Replace(Path.DirectorySeparatorChar, '/'));

        var picture = Assert.Single(q.Questions.Single().Pictures);
        Assert.Equal(Path.GetFullPath(png), Path.GetFullPath(picture.File));
        await DargApp.Settle(300);
        app.Shot("question-with-screenshot");
    }

    [AvaloniaFact]
    public async Task A_question_naming_a_missing_picture_or_none_shows_nothing_and_does_not_crash()
    {
        using var app = DargApp.Make();
        await app.Start();

        var missing = await Ask(app, "FAKE_ASK FAKE_ASK_IMAGE C:/nowhere/gone.png");
        Assert.False(missing.Questions.Single().HasPictures);
        missing.Questions.Single().Options[0].Command.Execute(null);

        app.Agents.NewPrompt = "FAKE_ASK";
        app.Agents.StartAgentCommand.Execute(null);
        await app.Until(() => app.Agents.Selected?.Items.OfType<QuestionItem>().Any() == true, "the second question");
        Assert.False(app.Agents.Selected!.Items.OfType<QuestionItem>().Single().Questions.Single().HasPictures);
        Assert.Equal("", app.Crashes());
    }

    [Fact]
    public void The_briefing_tells_agents_to_give_a_screenshots_path()
    {
        Assert.Contains("full path of a screenshot", Darg.Core.Decisions.Briefing);
    }
}
