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

    public static readonly Theme Dark = new("dark", "Dark", "The workshop at night: charcoal, amber.", false, "◆", new Dictionary<string, string>
    {
        ["Bg"] = "#14161a", ["Chrome"] = "#0f1114", ["Rail"] = "#111317", ["Panel"] = "#1b1e24", ["Panel2"] = "#22262e", ["Line"] = "#2e333d",
        ["Ink"] = "#ffffff", ["Dim"] = "#8b93a1", ["MdDim"] = "#a9b0bc",
        ["Accent"] = "#e0a84a", ["AccentHover"] = "#f0bc5e", ["OnAccent"] = "#1a1408", ["AccentSoft"] = "#3a3020", ["AccentWash"] = "#2a2416",
        ["Ok"] = "#5fbf7f", ["OkBg"] = "#1f2a22", ["OkLine"] = "#2f5e3f", ["Bad"] = "#e06464", ["Info"] = "#6aa5e8", ["StatusOff"] = "#5a606c",
        ["AddBg"] = "#1f3a27", ["AddInk"] = "#a8e6b8", ["DelBg"] = "#3a1f22", ["DelInk"] = "#f0a0a0",
        ["Sunk"] = "#101215", ["Sunk2"] = "#15171b", ["LogInk"] = "#cfcfcf", ["DirInk"] = "#c9a86a",
        ["UserBubble"] = "#2b2a22", ["UserBubbleLine"] = "#4a4330",
        ["LabelBg"] = "#2a2f39", ["LabelInk"] = "#c6ccd6", ["BugBg"] = "#4a2226", ["BugInk"] = "#ff8a8a",
        ["CardHoverLine"] = "#4a5160", ["Picked"] = "#f4f4f4", ["DropBg"] = "#20242c", ["WallDrop"] = "#22e0a84a", ["Wall"] = "#16181c",
        ["Lift"] = "#3a3d44", ["LiftLine"] = "#60ffffff",
    });

    public static readonly Theme Light = new("light", "Light", "Daylight: paper white, ink, a darker amber.", true, "◆", new Dictionary<string, string>
    {
        ["Bg"] = "#f6f7f9", ["Chrome"] = "#e9ecf0", ["Rail"] = "#eceff3", ["Panel"] = "#ffffff", ["Panel2"] = "#f0f2f5", ["Line"] = "#d5dae1",
        ["Ink"] = "#1b1e24", ["Dim"] = "#5f6773", ["MdDim"] = "#535b67",
        ["Accent"] = "#b8761a", ["AccentHover"] = "#9e6512", ["OnAccent"] = "#ffffff", ["AccentSoft"] = "#f7ead2", ["AccentWash"] = "#fbf3e4",
        ["Ok"] = "#2e8b57", ["OkBg"] = "#e2f4e8", ["OkLine"] = "#9fd3b0", ["Bad"] = "#c83c3c", ["Info"] = "#2f6fc0", ["StatusOff"] = "#a5acb8",
        ["AddBg"] = "#ddf3e3", ["AddInk"] = "#1e6b39", ["DelBg"] = "#fbe0e0", ["DelInk"] = "#a3262b",
        ["Sunk"] = "#eef0f3", ["Sunk2"] = "#f2f3f6", ["LogInk"] = "#2a2f38", ["DirInk"] = "#8a6420",
        ["UserBubble"] = "#f7efd9", ["UserBubbleLine"] = "#e2d3a8",
        ["LabelBg"] = "#e3e7ed", ["LabelInk"] = "#3a414d", ["BugBg"] = "#fbdcdc", ["BugInk"] = "#b3262d",
        ["CardHoverLine"] = "#a9b2c0", ["Picked"] = "#1b1e24", ["DropBg"] = "#e6ebf2", ["WallDrop"] = "#33b8761a", ["Wall"] = "#e4e7ec",
        ["Lift"] = "#ffffff", ["LiftLine"] = "#40000000",
    });

    public static readonly Theme Girlypop = new("girlypop", "Girlypop", "Bubblegum, lavender, hot pink, hearts.", true, "♡", new Dictionary<string, string>
    {
        ["Bg"] = "#fff0f6", ["Chrome"] = "#ffd6e8", ["Rail"] = "#ffe3f0", ["Panel"] = "#fff7fb", ["Panel2"] = "#ffe8f2", ["Line"] = "#f6bdd6",
        ["Ink"] = "#4a1838", ["Dim"] = "#a45b86", ["MdDim"] = "#8e4f77",
        ["Accent"] = "#ff3e9a", ["AccentHover"] = "#e62e86", ["OnAccent"] = "#ffffff", ["AccentSoft"] = "#ffd3e8", ["AccentWash"] = "#ffe6f2",
        ["Ok"] = "#2fae7f", ["OkBg"] = "#e3f8ee", ["OkLine"] = "#9fe0c3", ["Bad"] = "#e0294f", ["Info"] = "#9a5cf0", ["StatusOff"] = "#d6a6c1",
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
