using Workbench.Core;

namespace Workbench.Core.Tests;

public sealed class ClaudeDrawTests
{
    [Fact]
    public void Not_running_says_when_it_starts() =>
        Assert.Contains("not running", ClaudeDraw.Describe(null));

    [Fact]
    public void Describes_what_is_waiting_and_how_to_reach_it_from_another_device()
    {
        var s = new ClaudeDraw.State(Canvases: 1, Asking: true, Queued: 2, Unread: 1, Sessions: 3, Lan: true, Code: "123456", LanUrl: "http://192.168.1.20:7331/");
        Assert.Equal("1 canvas open · 3 waiting to be drawn · 1 sent, not yet read by an agent · 3 sessions · other devices: http://192.168.1.20:7331/ (code 123-456)",
            ClaudeDraw.Describe(s));
    }

    [Fact]
    public void Quiet_daemon_on_loopback()
    {
        var s = new ClaudeDraw.State(0, false, 0, 0, 1, false, "123456", null);
        Assert.Equal("no canvas open · 1 session", ClaudeDraw.Describe(s));
    }

    [Fact]
    public void Asking_to_draw_names_the_tools_and_carries_the_subject()
    {
        var blank = ClaudeDraw.AskToDraw("  ");
        Assert.Contains("request_drawing", blank);
        Assert.Contains("collect_drawing", blank);
        var about = ClaudeDraw.AskToDraw("the board's header spacing");
        Assert.Contains("the board's header spacing", about);
        Assert.Contains("image_path", about);
    }
}
