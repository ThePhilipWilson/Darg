using Workbench.Core;

namespace Workbench.Core.Tests;

public sealed class ChatTitleTests
{
    [Theory]
    [InlineData("can you sort these documents into folders please", "Sort these documents into folders please")]
    [InlineData("[Philip is looking at: Alpha › Docs, docs folder D:/x]\n\nok so add the label test to VOT-0003", "Add the label test to VOT-0003")]
    [InlineData("Which docs folder and which doc am I looking at, and how many docs are in that folder? Do not change anything.", "Which docs folder and which doc am I looking at…")]
    [InlineData("   \n  ", "Chat")]
    [InlineData("hi", "Chat")]
    public void Guesses_from_the_words(string message, string title) => Assert.Equal(title, ChatTitle.Guess(message));

    /// <summary>Real Haiku call. WORKBENCH_LIVE=1 to run it.</summary>
    [Fact]
    public async Task A_model_names_a_chat()
    {
        if (Environment.GetEnvironmentVariable("WORKBENCH_LIVE") != "1") return;
        var title = await ChatTitle.Make("[Philip is looking at: Alpha › Docs, docs folder D:/vault/Code/Alpha]\n\ncan you sort these documents into folders by topic please");
        Console.WriteLine("title: " + title);
        Assert.InRange(title.Split(' ').Length, 2, 8);
        Assert.DoesNotContain("Philip is looking", title);
    }

    [Theory]
    [InlineData("Sort Alpha docs into folders", "Sort Alpha docs into folders")]
    [InlineData("\"Tag deity cards\".\n", "Tag deity cards")]
    [InlineData("Title: count the docs", "Count the docs")]
    [InlineData("Sure! Here is a title for your request that describes what it is about in detail", null)]
    [InlineData("ok", null)]
    public void Cleans_a_models_reply(string reply, string? title) => Assert.Equal(title, ChatTitle.Clean(reply));
}
