using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>What the phone's read-only views of notes, docs and reports ask for (AND-0008): each as JSON, so a
/// thin client draws it without reading the desktop's files. Docs come with every link in them already
/// resolved (<see cref="DocLinks"/>), so [[Name]] works on a client that has no docs folder. Nothing here
/// writes, and a doc is only ever read from inside the project's docs folder.</summary>
public static partial class ReadViews
{
    public static JsonObject NotesList(string? folder)
    {
        var columns = new JsonArray();
        var notes = new JsonArray();
        if (folder is not null && Directory.Exists(folder))
        {
            foreach (var c in NoteStore.Columns(folder)) columns.Add(c);
            foreach (var n in NoteStore.List(folder))
                notes.Add(new JsonObject
                {
                    ["id"] = n.Id, ["column"] = n.Column, ["colour"] = n.Colour, ["created"] = n.Created, ["ticket"] = n.Ticket,
                    ["tags"] = new JsonArray(n.Tags.Select(t => (JsonNode?)t).ToArray()), ["text"] = n.Text,
                });
        }
        return new JsonObject { ["columns"] = columns, ["notes"] = notes };
    }

    /// <summary>Every doc in the folder (relative paths, forward slashes), as the Docs section lists them.</summary>
    public static List<string> DocList(string folder) =>
        !Directory.Exists(folder) ? [] :
        Directory.EnumerateFiles(folder, "*.md", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/'))
            .Where(f => !f.Split('/').Any(part => part.StartsWith('.')))
            .Order(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>One doc: its text and, for each link in it, where it goes: <c>doc</c> (path to ask for next),
    /// <c>external</c> (a web or mail address), <c>elsewhere</c> (a doc outside the folder, which the phone can't
    /// open) or <c>missing</c> (with the reason).</summary>
    public static JsonObject Doc(string folder, string path)
    {
        var docs = DocList(folder);
        var rel = path.Replace('\\', '/').TrimStart('/');
        var hit = docs.FirstOrDefault(d => d.Equals(rel, StringComparison.OrdinalIgnoreCase))
                  ?? docs.FirstOrDefault(d => d.Equals(rel + ".md", StringComparison.OrdinalIgnoreCase))
                  ?? throw new ApiError($"No doc \"{path}\". docs lists them.");
        var text = File.ReadAllText(Path.Combine(folder, hit));
        var links = new JsonObject();
        foreach (var raw in LinkTargets(text))
        {
            if (links.ContainsKey(raw)) continue;
            links[raw] = Describe(DocLinks.Resolve(raw, hit, docs, folder));
        }
        return new JsonObject { ["path"] = hit, ["title"] = Title(text, hit), ["text"] = text, ["links"] = links };
    }

    /// <summary>The link targets in a doc as <see cref="DocLinks.Resolve"/> takes them: "wiki:Name" for [[Name|text]],
    /// else the URL of [text](url). Links in code are left out.</summary>
    public static IEnumerable<string> LinkTargets(string text)
    {
        text = CodeRx().Replace(text, "");
        foreach (Match m in WikiRx().Matches(text))
        {
            var inner = m.Groups[1].Value;
            var bar = inner.IndexOf('|');
            yield return "wiki:" + (bar >= 0 ? inner[..bar] : inner).Trim();
        }
        foreach (Match m in MdLinkRx().Matches(text))
        {
            var url = m.Groups[1].Value.Trim();
            if (url.Length > 0 && !url.StartsWith('#')) yield return url;
        }
    }

    static JsonObject Describe(DocLink l) => l switch
    {
        DocLink.Doc d => new JsonObject { ["kind"] = "doc", ["path"] = d.Path },
        DocLink.External e => new JsonObject { ["kind"] = "external", ["target"] = e.Target },
        DocLink.Elsewhere w => new JsonObject { ["kind"] = "elsewhere", ["name"] = Path.GetFileName(w.FullPath) },
        DocLink.Missing m => new JsonObject { ["kind"] = "missing", ["reason"] = m.Reason },
        _ => new JsonObject { ["kind"] = "missing" },
    };

    static string Title(string text, string path) =>
        HeadingRx().Match(text) is { Success: true } h ? h.Groups[1].Value.Trim() : Path.GetFileNameWithoutExtension(path);

    public static JsonArray ReportList(string folder, bool unreadOnly = false)
    {
        var all = ReportStore.List(folder).Where(r => !unreadOnly || !r.Read);
        return new JsonArray(all.Select(r => (JsonNode?)Summary(r)).ToArray());
    }

    static JsonObject Summary(AgentReport r) => new()
    {
        ["id"] = r.Id, ["card"] = r.Card, ["title"] = r.Heading, ["kind"] = r.Kind.ToString().ToLowerInvariant(),
        ["date"] = r.Date, ["read"] = r.Read, ["commits"] = new JsonArray(r.Commits.Select(c => (JsonNode?)c).ToArray()),
    };

    /// <summary>One report by id (its file name without .md): its details and the body below the properties.</summary>
    public static JsonObject Report(string folder, string id)
    {
        var file = Path.Combine(folder, Path.GetFileName(id.Trim()) + (id.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? "" : ".md"));
        if (!File.Exists(file)) throw new ApiError($"No report \"{id}\". reports lists them.");
        var r = ReportStore.Parse(File.ReadAllText(file), file);
        var o = Summary(r);
        o["agent"] = r.Agent; o["repo"] = r.Repo; o["body"] = r.Body;
        return o;
    }

    [GeneratedRegex(@"\[\[([^\[\]\r\n]+)\]\]")] private static partial Regex WikiRx();
    [GeneratedRegex(@"(?<!!)\[[^\]\r\n]*\]\(([^)\r\n]+)\)")] private static partial Regex MdLinkRx();
    [GeneratedRegex(@"```.*?```|`[^`\r\n]*`", RegexOptions.Singleline)] private static partial Regex CodeRx();
    [GeneratedRegex(@"^#\s+(.+)$", RegexOptions.Multiline)] private static partial Regex HeadingRx();
}
