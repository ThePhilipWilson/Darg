using System.Text.Json;
using System.Text.Json.Serialization;

namespace Darg.Core;

/// <summary>A look for the whole app: every colour the views name by key (<c>Bg</c>, <c>Accent</c>,
/// <c>Panel</c>…), whether Fluent's own controls draw light or dark beneath it, and the glyph beside
/// the name in the header.</summary>
public sealed record Theme(string Id, string Name, string Blurb, bool IsLight, string Mark, IReadOnlyDictionary<string, string> Colours)
{
    /// <summary>The accent Fluent's own controls use (checkboxes, focus rings, selection, the picked rail
    /// button); null keeps the system accent. Only the accent: Fluent's other colours are fixed once its
    /// light or dark brushes are first drawn, so they couldn't follow a switch.</summary>
    public string? FluentAccent { get; init; }
}

/// <summary>The themes Settings offers. Each names a colour for every key in <see cref="Keys"/>; the app
/// puts them in its resources as brushes, and views take them with DynamicResource so a switch shows at
/// once.</summary>
public static class Themes
{
    /// <summary>What each key is for, so a new theme knows what it is colouring.</summary>
    public static readonly IReadOnlyDictionary<string, string> Keys = new Dictionary<string, string>
    {
        ["Bg"] = "the window behind everything",
        ["Chrome"] = "the header and status bar",
        ["Rail"] = "a project's section rail",
        ["Panel"] = "boxes, lists and headers on the page",
        ["Panel2"] = "a card on a board, a table's header row",
        ["Line"] = "borders and dividers",
        ["Ink"] = "ordinary text",
        ["Dim"] = "quieter text: hints, dates, counts",
        ["MdDim"] = "quotes and list marks in markdown",
        ["Accent"] = "the app's colour: primary buttons, needs-you, highlights",
        ["AccentHover"] = "a filled accent chip under the pointer",
        ["OnAccent"] = "text on anything filled with the accent",
        ["AccentSoft"] = "a chip or tag tinted with the accent",
        ["AccentWash"] = "a question or permission waiting for you; an agent row that needs you",
        ["Ok"] = "running, done, passed",
        ["OkBg"] = "a box with good news in it",
        ["OkLine"] = "that box's border",
        ["Bad"] = "failed, errors",
        ["Warn"] = "middling: an amber ticket score",
        ["Info"] = "working, links, tool names",
        ["StatusOff"] = "a dot for stopped or saved",
        ["AddBg"] = "an added line in a diff",
        ["AddInk"] = "its text",
        ["DelBg"] = "a removed line in a diff",
        ["DelInk"] = "its text",
        ["Sunk"] = "code blocks and tool results",
        ["Sunk2"] = "a tool's input preview",
        ["LogInk"] = "a dev server's log",
        ["DirInk"] = "a folder's name",
        ["UserBubble"] = "what you said, in a transcript",
        ["UserBubbleLine"] = "its border",
        ["LabelBg"] = "a label or kind tag on a card; inline code",
        ["LabelInk"] = "its text",
        ["BugBg"] = "a bug's kind tag",
        ["BugInk"] = "its text",
        ["CardHoverLine"] = "a card's border under the pointer",
        ["Picked"] = "the border round a picked card or post-it",
        ["DropBg"] = "a column a dragged card would land in",
        ["WallDrop"] = "a notes-wall column a post-it would land in",
        ["Wall"] = "behind the notes wall",
        ["Lift"] = "a card being dragged",
        ["LiftLine"] = "its border",
    };

    public static readonly Theme Dark = new("dark", "Dark", "Ink-black with a violet cast, the logo's hot pink and teal.", false, "◆", new Dictionary<string, string>
    {
        ["Bg"] = "#12111a", ["Chrome"] = "#0c0b12", ["Rail"] = "#0f0e16", ["Panel"] = "#1a1824", ["Panel2"] = "#232031", ["Line"] = "#332f45",
        ["Ink"] = "#ffffff", ["Dim"] = "#9a95b0", ["MdDim"] = "#b5b0c8",
        ["Accent"] = "#d74f9d", ["AccentHover"] = "#e56fb2", ["OnAccent"] = "#14040d", ["AccentSoft"] = "#371f30", ["AccentWash"] = "#281a23",
        ["Ok"] = "#34c4bf", ["OkBg"] = "#14302f", ["OkLine"] = "#25706d", ["Bad"] = "#ff6b4a", ["Warn"] = "#f2b33d", ["Info"] = "#8aa8ff", ["StatusOff"] = "#5b5770",
        ["AddBg"] = "#123a38", ["AddInk"] = "#8fe6e2", ["DelBg"] = "#3a1426", ["DelInk"] = "#ff9fc4",
        ["Sunk"] = "#0d0c14", ["Sunk2"] = "#121019", ["LogInk"] = "#d6d2e6", ["DirInk"] = "#6fd6d2",
        ["UserBubble"] = "#2b1a26", ["UserBubbleLine"] = "#55304a",
        ["LabelBg"] = "#2a2638", ["LabelInk"] = "#cfc9e6", ["BugBg"] = "#4a1a2f", ["BugInk"] = "#ff8fb8",
        ["CardHoverLine"] = "#5a4f7a", ["Picked"] = "#34c4bf", ["DropBg"] = "#1e1b2b", ["WallDrop"] = "#33d74f9d", ["Wall"] = "#151320",
        ["Lift"] = "#2f2b40", ["LiftLine"] = "#60ffffff",
    })
    { FluentAccent = "#d74f9d" };

    public static readonly Theme Light = new("light", "Light", "Paper with a blush, ink-violet text, a deeper pink and teal.", true, "◆", new Dictionary<string, string>
    {
        ["Bg"] = "#fbf6fa", ["Chrome"] = "#f1e6ef", ["Rail"] = "#f4eaf2", ["Panel"] = "#ffffff", ["Panel2"] = "#f7eef5", ["Line"] = "#e2d3df",
        ["Ink"] = "#1a1220", ["Dim"] = "#6b5d72", ["MdDim"] = "#5a4c62",
        ["Accent"] = "#c13c88", ["AccentHover"] = "#a52f73", ["OnAccent"] = "#ffffff", ["AccentSoft"] = "#f9dfee", ["AccentWash"] = "#fcf0f7",
        ["Ok"] = "#1a8783", ["OkBg"] = "#dcf6f4", ["OkLine"] = "#8fdcd7", ["Bad"] = "#d6402a", ["Warn"] = "#b97c00", ["Info"] = "#4a5fd0", ["StatusOff"] = "#b6a9bd",
        ["AddBg"] = "#d8f4f1", ["AddInk"] = "#0a6b67", ["DelBg"] = "#fde0ea", ["DelInk"] = "#a3194f",
        ["Sunk"] = "#f1e8ef", ["Sunk2"] = "#f6eef4", ["LogInk"] = "#2a1f31", ["DirInk"] = "#1a8783",
        ["UserBubble"] = "#fbe6f2", ["UserBubbleLine"] = "#efbcd9",
        ["LabelBg"] = "#eadfe8", ["LabelInk"] = "#43364b", ["BugBg"] = "#fddbe6", ["BugInk"] = "#b01a4a",
        ["CardHoverLine"] = "#c5a9bd", ["Picked"] = "#34c4bf", ["DropBg"] = "#f2e4ef", ["WallDrop"] = "#33c13c88", ["Wall"] = "#efe3ec",
        ["Lift"] = "#ffffff", ["LiftLine"] = "#40000000",
    })
    { FluentAccent = "#c13c88" };

    public static readonly Theme Girlypop = new("girlypop", "Girlypop", "Bubblegum, lavender, hot pink, hearts.", true, "♡", new Dictionary<string, string>
    {
        ["Bg"] = "#fff0f6", ["Chrome"] = "#ffd6e8", ["Rail"] = "#ffe3f0", ["Panel"] = "#fff7fb", ["Panel2"] = "#ffe8f2", ["Line"] = "#f6bdd6",
        ["Ink"] = "#4a1838", ["Dim"] = "#a45b86", ["MdDim"] = "#8e4f77",
        ["Accent"] = "#ff3e9a", ["AccentHover"] = "#e62e86", ["OnAccent"] = "#ffffff", ["AccentSoft"] = "#ffd3e8", ["AccentWash"] = "#ffe6f2",
        ["Ok"] = "#2fae7f", ["OkBg"] = "#e3f8ee", ["OkLine"] = "#9fe0c3", ["Bad"] = "#e0294f", ["Warn"] = "#e08a00", ["Info"] = "#9a5cf0", ["StatusOff"] = "#d6a6c1",
        ["AddBg"] = "#ddf7ea", ["AddInk"] = "#1d7a55", ["DelBg"] = "#ffd9e0", ["DelInk"] = "#b0173d",
        ["Sunk"] = "#fbe6f0", ["Sunk2"] = "#fdeef5", ["LogInk"] = "#4a1838", ["DirInk"] = "#b2457f",
        ["UserBubble"] = "#f6e2ff", ["UserBubbleLine"] = "#e4b5f5",
        ["LabelBg"] = "#fcd5ea", ["LabelInk"] = "#7a2c5d", ["BugBg"] = "#ffd0dc", ["BugInk"] = "#c01848",
        ["CardHoverLine"] = "#ee8fbb", ["Picked"] = "#9a5cf0", ["DropBg"] = "#ffe0ee", ["WallDrop"] = "#33ff3e9a", ["Wall"] = "#ffe9f3",
        ["Lift"] = "#fff7fb", ["LiftLine"] = "#60ff3e9a",
    })
    { FluentAccent = "#ff3e9a" };

    public static readonly IReadOnlyList<Theme> All = [Dark, Light, Girlypop];

    /// <summary>The theme with this id (ignoring case), else Dark.</summary>
    public static Theme Get(string? id) => All.FirstOrDefault(t => string.Equals(t.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Dark;

    /// <summary>The theme with this id or name, or null: for the API, where a typo should say so rather than go dark.</summary>
    public static Theme? Find(string? idOrName) => All.FirstOrDefault(t =>
        string.Equals(t.Id, idOrName?.Trim(), StringComparison.OrdinalIgnoreCase) || string.Equals(t.Name, idOrName?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>The app's own settings for an instance (the theme so far), in <c>settings.json</c> beside
/// instance.json: kept apart from that file, which people and Fettle edit, as <see cref="LastProject"/> is.</summary>
public sealed class AppSettings
{
    [JsonPropertyName("theme")] public string Theme { get; set; } = Darg.Core.Themes.Dark.Id;
    /// <summary>The header's Optimise tokens toggle (<see cref="TokenSaver"/>).</summary>
    [JsonPropertyName("optimiseTokens")] public bool OptimiseTokens { get; set; }

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string FilePath(string? instanceFolder = null) => Path.Combine(instanceFolder ?? InstanceConfig.Folder(), "settings.json");

    /// <summary>The saved settings, or the defaults when there are none or the file can't be read.</summary>
    public static AppSettings Load(string? instanceFolder = null)
    {
        try
        {
            var path = FilePath(instanceFolder);
            return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new() : new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    /// <summary>Saves; null when it worked, else why not.</summary>
    public string? Save(string? instanceFolder = null)
    {
        try
        {
            var path = FilePath(instanceFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return e.Message; }
    }
}
