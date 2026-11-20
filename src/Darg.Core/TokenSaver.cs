namespace Darg.Core;

/// <summary>Optimise tokens (WOR-0159): the header's toggle for when the usage limit is near. While it is on, every
/// agent runs one model down (Fable on Opus, Opus on Sonnet, Sonnet on Haiku; Haiku stays) and at no more than
/// medium effort, from its next turn; turned off, each goes back to what it had. What an agent was given (its
/// profile's pick, or one made by hand) is kept as it was: this only changes what it runs on. A model or effort
/// picked by hand on an agent is left alone. Kept in settings.json, so it survives a restart.</summary>
public static class TokenSaver
{
    static volatile bool _on;
    static bool _loaded;

    /// <summary>Whether it is on. Read from settings.json the first time it's asked.</summary>
    public static bool On
    {
        get
        {
            if (!_loaded) Load();
            return _on;
        }
    }

    /// <summary>Read the instance's setting (again): the app does this as it starts, so a test's instance starts as its settings say.</summary>
    public static void Load(string? instanceFolder = null)
    {
        _on = AppSettings.Load(instanceFolder).OptimiseTokens;
        _loaded = true;
    }

    /// <summary>Turn it on or off and save that; null when saved, else why it wasn't (it is switched all the same).</summary>
    public static string? Set(bool on, string? instanceFolder = null)
    {
        _on = on;
        _loaded = true;
        var settings = AppSettings.Load(instanceFolder);
        if (settings.OptimiseTokens == on) return null;
        settings.OptimiseTokens = on;
        return settings.Save(instanceFolder);
    }

    /// <summary>The model families, cheapest first.</summary>
    static readonly string[] Ladder = ["haiku", "sonnet", "opus", "fable"];

    /// <summary>The model one step down from <paramref name="model"/> (an alias or an id: "opus", "claude-opus-5-5",
    /// "opus[1m]"). Null or "default" is the person's default, <paramref name="defaultModel"/> when known, else taken to be
    /// Opus (Claude Code's default on a subscription). Haiku, and a model of no family it knows, stay as they are.</summary>
    public static string? LowerModel(string? model, string? defaultModel = null)
    {
        var onDefault = string.IsNullOrWhiteSpace(model) || model == AgentChoices.Default;
        var name = onDefault ? defaultModel ?? "opus" : model!;
        var rung = Array.FindIndex(Ladder, f => name.Contains(f, StringComparison.OrdinalIgnoreCase));
        if (rung <= 0) return onDefault ? null : model;
        return Ladder[rung - 1];
    }

    /// <summary>The effort capped at medium: high, xhigh, max and the default become medium; low and medium stay.</summary>
    public static string? LowerEffort(string? effort) =>
        effort is "low" or "medium" ? effort : "medium";

    /// <summary>What an agent given <paramref name="model"/> and <paramref name="effort"/> runs on: lowered while it is on,
    /// except a pick made by hand. Null is the person's default, as for the agent's own choices.</summary>
    public static (string? Model, string? Effort) Running(string? model, string? effort, string? defaultModel = null,
        bool modelByHand = false, bool effortByHand = false, bool? on = null)
    {
        if (!(on ?? On)) return (model, effort);
        return (modelByHand ? model : LowerModel(model, defaultModel), effortByHand ? effort : LowerEffort(effort));
    }

    /// <summary>"sonnet, effort medium"; "default model" for a null model.</summary>
    public static string Describe(string? model, string? effort) =>
        $"{(string.IsNullOrEmpty(model) || model == AgentChoices.Default ? "default model" : model)}, effort {(string.IsNullOrEmpty(effort) || effort == AgentChoices.Default ? "default" : effort)}";

    /// <summary>The header toggle's tooltip.</summary>
    public const string Tip = "Optimise tokens: while it's on, every agent runs one model down (Fable on Opus, Opus on Sonnet, " +
        "Sonnet on Haiku) and at no more than medium effort, from its next turn; running agents switch too. Turn it off and each " +
        "goes back to what it had. A model or effort you picked by hand on an agent is left alone.";
}
