using System.Diagnostics;
using Workbench.Core;

namespace Workbench.Core.Tests;

public sealed class ToolsTests
{
    [Fact]
    public async Task A_tool_that_isnt_running_is_a_status_not_a_failure()
    {
        var sw = Stopwatch.StartNew();
        foreach (var kind in new[] { "forge", "comfyui", "ollama", "web" })
        {
            var s = await Tools.Check(new ToolConfig { Kind = kind, Url = "http://127.0.0.1:9" }); // nothing listens on port 9
            Assert.Equal((false, "not running"), (s.Up, s.Detail));
        }
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15));
    }

    [Theory]
    [InlineData("nomic-embed-text:latest", "nomic-bert", true)]
    [InlineData("mxbai-embed-large", "bert", true)]
    [InlineData("all-minilm", "bert", true)]
    [InlineData("mistral-nemo:12b", "llama", false)]
    [InlineData("laya:0.3.7", "qwen2", false)]
    public void Embedding_models_are_told_apart(string name, string family, bool embedding) =>
        Assert.Equal(embedding, Tools.IsEmbedding(name, family));

    [Fact]
    public void The_usual_four_when_none_are_set() =>
        Assert.Equal(["forge", "comfyui", "ollama", "pinokio"], new InstanceConfig().ToolsOrDefaults().Select(t => t.Id));

    /// <summary>Real Forge. WORKBENCH_LIVE_FORGE=url to run it (a 256px, 6-step image).</summary>
    [Fact]
    public async Task Forge_answers_and_draws()
    {
        var url = Environment.GetEnvironmentVariable("WORKBENCH_LIVE_FORGE");
        if (string.IsNullOrEmpty(url)) return;
        var s = await Tools.Check(new ToolConfig { Kind = "forge", Url = url });
        Assert.True(s.Up, s.Detail);
        var (png, error) = await Tools.ForgeTxt2Img(url, "a red apple on a table", "", 256, 256, 6);
        Assert.True(png is { Length: > 1000 }, error);
        Assert.Equal(0x89, png![0]); // PNG signature
    }
}
