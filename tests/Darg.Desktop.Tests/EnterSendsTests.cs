using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Darg.Desktop.Views;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0188: every box that sends something sends on Enter; Shift+Enter, or a typed backslash then Enter, is a
/// new line; Enter in a blank box does nothing. Driven through real key presses on the real window.</summary>
public class EnterSendsTests
{
    static TextBox Box(DargApp app, Func<TextBox, bool> which) =>
        app.Window.GetVisualDescendants().OfType<TextBox>().First(b => b.IsEffectivelyVisible && which(b));

    static void Type(DargApp app, TextBox box, string text)
    {
        box.Focus();
        box.CaretIndex = (box.Text ?? "").Length;
        app.Window.KeyTextInput(text);
    }

    static void Enter(DargApp app, TextBox box, bool shift = false)
    {
        box.Focus();
        app.Window.KeyPress(Key.Enter, shift ? RawInputModifiers.Shift : RawInputModifiers.None, PhysicalKey.Enter, null);
    }

    static string Lines(string? text) => (text ?? "").Replace("\r\n", "\n");

    /// <summary>The whole rule on one box: blank Enter, Shift+Enter, backslash-Enter, then Enter sends what's typed;
    /// <paramref name="sent"/> says whether it has gone (checked false after each step that mustn't send).</summary>
    static async Task Exercise(DargApp app, TextBox box, Func<bool> sent, string what)
    {
        Assert.True(EnterSends.GetCommand(box) is not null, $"{what} has no EnterSends command");
        Enter(app, box);
        await DargApp.Settle();
        Assert.Equal("", Lines(box.Text)); // Enter in a blank box adds no line…
        Assert.False(sent(), $"{what}: Enter in a blank box sent");  // …and sends nothing

        Type(app, box, "first line");
        Enter(app, box, shift: true);
        await DargApp.Settle();
        Assert.Equal("first line\n", Lines(box.Text));
        Assert.False(sent(), $"{what}: Shift+Enter sent");

        Type(app, box, "second\\");
        Enter(app, box);
        await DargApp.Settle();
        Assert.Equal("first line\nsecond\n", Lines(box.Text));
        Assert.False(sent(), $"{what}: backslash-Enter sent");

        Type(app, box, "third");
        Enter(app, box);
        await app.Until(sent, $"{what}: Enter to send");
        Assert.Equal("", Lines(box.Text));
    }

    [AvaloniaFact]
    public async Task Enter_sends_in_the_new_agent_box_and_the_agent_box_with_Shift_or_backslash_for_a_new_line()
    {
        using var app = DargApp.Make();
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Agents" });
        await DargApp.Settle();

        var prompt = Box(app, b => Avalonia.Automation.AutomationProperties.GetName(b) == "New agent prompt");
        await Exercise(app, prompt, () => app.Agents.Agents.Count > 0, "the new agent box");
        await app.Until(() => app.Prompts().Count > 0, "the new agent's prompt to reach claude");
        Assert.Contains("first line\nsecond\nthird", Lines(app.Prompts()[0]));

        await app.Until(() => app.Agents.Shown is not null, "the new agent shown");
        await DargApp.Settle(300);
        var compose = Box(app, b => b.Name == "ComposeBox");
        var before = app.Prompts().Count;
        await Exercise(app, compose, () => app.Prompts().Count > before, "the agent's message box");
        Assert.Contains("first line\nsecond\nthird", Lines(app.Prompts()[before]));
        app.Shot("agents");
    }

    [AvaloniaFact]
    public async Task Enter_sends_in_Fettles_box_with_Shift_or_backslash_for_a_new_line()
    {
        using var app = DargApp.Make();
        await app.Start();
        app.Main.Mascot.Open = true;
        await DargApp.Settle();

        var box = Box(app, b => b.Name == "MascotBox");
        await Exercise(app, box, () => app.Prompts().Any(p => Lines(p).Contains("third")), "Fettle's box");
        Assert.Contains(app.Prompts(), p => Lines(p).Contains("first line\nsecond\nthird"));
        app.Shot("fettle");
    }
}
