using System.Text.Json.Nodes;
using Darg.Core;
using Xunit;

namespace Darg.Core.Tests;

/// <summary>AND-0006: phone notifications through ntfy, a card's decision options as buttons whose taps come back
/// through a reply topic and answer the card once.</summary>
public class PushTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("darg-push-").FullName;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    static readonly CardDecision Four = CardDecision.Parse(
        "**Question:** Ship it?\n" +
        "- **No** → back: more work\n" +
        "- **Yes** (recommended) → done: it works\n" +
        "- **Later** → stay: leave it\n" +
        "- **Ask Fettle**: the agent decides\n" +
        "**Context:** tests pass\n");

    static PushConfig Config(string server = "https://ntfy.example", string? token = null) => new() { Server = server + "/", Topic = "darg-abc", Token = token };

    [Fact]
    public void A_decision_shows_at_most_three_buttons_recommended_first_each_posting_its_own_option()
    {
        var store = new PushStore(_dir);
        var pending = store.Add("proj", "AND-0006", Four.Options.Select(o => o.Label), DateTime.Now);
        var msg = PushStore.DecisionMessage(Config(), pending, "Proj", "Phone push", Four);

        Assert.Equal("darg-abc", (string?)msg["topic"]);
        Assert.Equal("Proj: AND-0006 Phone push", (string?)msg["title"]);
        Assert.Equal("proj-and-0006", (string?)msg["sequence_id"]);
        Assert.Contains("Ship it?", (string?)msg["message"]);
        Assert.Contains("tests pass", (string?)msg["message"]);
        Assert.Contains("Ask Fettle (answer on the desktop)", (string?)msg["message"]); // the fourth option has no button

        var actions = msg["actions"]!.AsArray().Select(a => a!.AsObject()).ToList();
        Assert.Equal(3, actions.Count);
        Assert.Equal(["Yes ★", "No", "Later"], actions.Select(a => (string?)a["label"]));
        Assert.All(actions, a =>
        {
            Assert.Equal("http", (string?)a["action"]);
            Assert.Equal("https://ntfy.example/darg-abc-reply", (string?)a["url"]);
            Assert.True((bool)a["clear"]!);
            Assert.Null(a["headers"]);
        });
        // each button's body names the option it stands for, whatever order the buttons are in (a tap forgets the key, so a fresh send each time)
        for (var i = 0; i < actions.Count; i++)
        {
            pending = store.Add("proj", "AND-0006", Four.Options.Select(o => o.Label), DateTime.Now);
            var button = PushStore.DecisionMessage(Config(), pending, "Proj", "Phone push", Four)["actions"]![i]!;
            Assert.Equal(((string)button["label"]!).Replace(" ★", ""), store.Take(null, (string)button["body"]!)?.Label);
        }
    }

    [Fact]
    public void A_token_goes_with_each_button_so_a_protected_reply_topic_takes_the_tap()
    {
        var store = new PushStore(_dir);
        var pending = store.Add("proj", "C-1", ["Yes", "No"], DateTime.Now);
        var msg = PushStore.DecisionMessage(Config(token: "tk_x"), pending, "Proj", "t", CardDecision.Parse("**Question:** q\n- **Yes** → done\n- **No** → back\n"));
        Assert.All(msg["actions"]!.AsArray(), a => Assert.Equal("Bearer tk_x", (string?)a!["headers"]!["Authorization"]));
    }

    [Fact]
    public void A_tap_answers_once_and_a_made_up_or_stale_key_answers_nothing()
    {
        var store = new PushStore(_dir);
        var first = store.Add("proj", "C-1", ["Yes", "No"], DateTime.Now);
        var second = store.Add("proj", "C-1", ["Yes", "No"], DateTime.Now); // the decision was sent again: the old buttons stop working

        Assert.Null(store.Take("a", $"darg {first.Key} 0"));
        Assert.Null(store.Take("b", $"darg {second.Key} 5"));  // no such option
        Assert.Null(store.Take("c", "darg 0123456789abcdef0123 0")); // nobody's key
        Assert.Null(store.Take("d", "hello"));                 // something else posted to the topic
        Assert.Equal(new PushAnswer(second.Key, "proj", "C-1", "No"), store.Take("e", $"darg {second.Key} 1"));
        Assert.Null(store.Take("f", $"darg {second.Key} 1"));  // a second tap, or ntfy replaying it
        Assert.Equal("f", store.Since);
    }

    [Fact]
    public void Keys_and_where_the_reply_topic_was_read_to_survive_a_restart_and_answering_on_the_desktop_forgets_the_key()
    {
        var store = new PushStore(_dir);
        var a = store.Add("proj", "C-1", ["Yes", "No"], DateTime.Now);
        var b = store.Add("proj", "C-2", ["Yes", "No"], DateTime.Now);
        store.Take("m7", "unrelated");

        var again = new PushStore(_dir);
        Assert.Equal("m7", again.Since);
        Assert.True(again.Forget("proj", "c-1")); // answered on the desktop
        Assert.False(again.Forget("proj", "C-1"));
        Assert.Null(again.Take(null, $"darg {a.Key} 0"));
        Assert.Equal("C-2", again.Take(null, $"darg {b.Key} 0")?.Card);
    }

    [Fact]
    public void Old_keys_go_once_they_are_past_keeping()
    {
        var store = new PushStore(_dir);
        var old = store.Add("proj", "C-1", ["Yes", "No"], DateTime.Now - PushStore.Keep - TimeSpan.FromHours(1));
        store.Add("proj", "C-2", ["Yes", "No"], DateTime.Now);
        Assert.DoesNotContain(store.Pending, p => p.Key == old.Key);
    }

    [Fact]
    public void A_new_topic_is_long_and_random()
    {
        var a = PushConfig.NewTopic();
        Assert.StartsWith("darg-", a);
        Assert.True(a.Length >= 25);
        Assert.NotEqual(a, PushConfig.NewTopic());
        Assert.False(new PushConfig().On);
        Assert.Equal("https://ntfy.sh", new PushConfig { Server = "" }.Base);
    }

    [Fact]
    public void Push_settings_are_kept_in_instance_json_and_left_out_when_off()
    {
        var config = new InstanceConfig();
        config.Save(_dir);
        Assert.DoesNotContain("push", File.ReadAllText(InstanceConfig.FilePath(_dir)));
        config.Push = new PushConfig { Topic = "darg-x" };
        config.Save(_dir);
        Assert.Equal("darg-x", InstanceConfig.Load(_dir).Push?.Topic);
    }

    [Fact]
    public async Task The_client_publishes_hears_a_tap_on_the_reply_topic_and_deletes_the_notification_once_answered()
    {
        using var ntfy = new FakeNtfy();
        var store = new PushStore(_dir);
        using var client = new PushClient(new PushConfig { Server = ntfy.Url, Topic = "darg-t" }, store) { Retry = TimeSpan.FromMilliseconds(100) };
        var answers = new List<PushAnswer>();
        client.Answered += a => { lock (answers) answers.Add(a); };
        client.Listen();

        Assert.Null(await client.SendDecision("proj", "Proj", "C-9", "Ship", Four));
        var sent = Assert.Single(ntfy.Snapshot());
        var yes = sent["actions"]![0]!["body"]!.ToString();
        await Until(() => ntfy.StreamCount > 0);
        Assert.StartsWith("/darg-t-reply/json", ntfy.Streams[0]);

        var id = ntfy.Reply(yes);
        await Until(() => { lock (answers) return answers.Count > 0; });
        Assert.Equal("Yes", answers.Single().Label);
        Assert.Equal(id, new PushStore(_dir).Since); // a restart reads on from here

        // a second decision answered on the desktop: buttons dead, notification deleted
        await client.SendDecision("proj", "Proj", "C-10", "Other", Four);
        await client.Settled("proj", "C-10");
        Assert.Equal(["darg-t/proj-c-10"], ntfy.DeletedSnapshot());
    }

    [Fact]
    public async Task A_refused_publish_is_reported_not_thrown()
    {
        using var ntfy = new FakeNtfy { PublishStatus = 400 };
        using var client = new PushClient(new PushConfig { Server = ntfy.Url, Topic = "darg-t" }, new PushStore(_dir));
        string? problem = null;
        client.Problem += p => problem = p;
        var why = await client.Send(PushStore.PlainMessage(client.Config, "t", "d", false, "x"));
        Assert.Contains("400", why);
        Assert.Equal(why, problem);
    }

    static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(50);
        Assert.True(condition());
    }
}
