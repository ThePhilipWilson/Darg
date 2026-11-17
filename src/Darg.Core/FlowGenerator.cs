namespace Darg.Core;

/// <summary>A basic flow works on code and docs; an art flow makes pictures with the local image tools.</summary>
public enum FlowKind { Basic, Art }

/// <summary>The flow generator (WOR-0077): an agent writes a new flow file from a few words, for one project
/// (its own flows folder) or shared by every project (the instance's), a basic flow or an art one. It reads
/// the project and the flows already on file, asks when the words could make quite different flows, and
/// writes one markdown file in <see cref="FlowStore"/>'s format.</summary>
public static class FlowGenerator
{
    /// <summary>What the agent is asked.</summary>
    /// <param name="description">What the flow is for, in Philip's words; empty: the agent asks.</param>
    /// <param name="folder">Where the new flow goes: the project's flows folder or the shared one.</param>
    /// <param name="shared">Whether <paramref name="folder"/> is the shared one.</param>
    /// <param name="others">The flows folders to read first (the shared one and the project's), so it doesn't write one twice.</param>
    /// <param name="tools">The local tools on this machine (art flows): name and address.</param>
    public static string Prompt(string projectName, FlowKind kind, string description, string folder, bool shared,
        IReadOnlyList<string> others, IReadOnlyList<ToolConfig>? tools = null)
    {
        var art = kind == FlowKind.Art;
        var lines = new List<string>
        {
            $"Write a new {(art ? "art " : "")}flow for Darg, " + (shared ? "shared by every project (it was asked for from " + projectName + ")." : $"for the {projectName} project only."),
            "",
            "A flow is a team of agents run one after another on a task, fully automatic: each stage is its own agent with no memory of the others, " +
            "handed the task and the last message of every stage before it. A checking stage can send the work back to an earlier one until it passes.",
            "",
        };
        if (description.Trim().Length > 0)
        {
            lines.Add("What it's for, in my words:");
            lines.Add("> " + description.Trim().Replace("\n", "\n> "));
        }
        else lines.Add("I haven't said what it's for: ask me with AskUserQuestion, with two or three flows this project could use as the options.");
        lines.AddRange([
            "",
            "1. Read the project's CLAUDE.md and enough of the project to know what its work looks like. Read the flows already on file (" +
            string.Join(", ", others) + "): if one already does this, say so and ask whether to change that one instead.",
            "2. If my words could make quite different flows (which roles, whether something checks the work, how many rounds), ask me with AskUserQuestion " +
            "before writing. Put your readings in the question itself, since I may only see the question: your best reading first, marked (Recommended). " +
            "Don't pick one quietly.",
            $"3. Write the flow as one markdown file in {folder}, named for the flow in lower-case words joined by hyphens (designer-engineer-qa.md). " +
            "Don't overwrite a file already there. The format:",
            "   - `# Name` on the first line: what it shows as in the Flow list.",
            "   - One line saying what it's for, then `Max rounds: n` (how many times a checker can send work back; 1 to 3).",
            "   - A `## Role` section per stage, in order, with that stage's brief under it.",
            "   - A stage that checks another's work starts its brief with `Sends back to: <an earlier role>`. Darg tells it to end with `VERDICT: PASS` or " +
            "`VERDICT: FAIL` and to stop the flow with `FLOW: STOP` when it can't succeed, so the brief needn't.",
            "   Here is the starter flow, as an example of the format and the tone:",
            "",
            .. FlowStore.Starter.Split('\n').Select(l => "   " + l.TrimEnd('\r')),
            "",
            "   What makes a good flow:",
            "   - Two to four stages. Each brief a few lines, plain words, written to the agent (\"Read…\", \"Don't change any code\").",
            "   - Every brief says what that stage hands on in its last message, since that message is all the next stage gets.",
            "   - No stage can wait for me mid-flow. Anything for me to decide or pick comes at the end, from the last stage.",
            "   - Stages that change files follow the project's CLAUDE.md, its commit rules included: point to it, don't restate it.",
            shared ? "   - It's shared, so keep the briefs true for any project: name no repo, path or tool only one project has."
                   : "   - It's this project's own, so the briefs can name its files, commands, skills and conventions.",
        ]);
        if (art)
        {
            lines.AddRange([
                "",
                "   It's an art flow: its stages make pictures with the local image tools rather than code.",
                "   - The tools on this machine: " + (tools is { Count: > 0 }
                    ? string.Join("; ", tools.Where(t => t.Kind is "forge" or "comfyui").Select(t => $"{t.Name} ({t.Kind}) at {t.Url}"))
                    : "Forge at http://127.0.0.1:7860") + ". Forge is driven through its API (/sdapi/v1/txt2img and the rest).",
                "   - If the project has an art recipe of its own (an art skill under .claude/skills, or notes in its CLAUDE.md or docs), the stages that " +
                "generate follow it by name rather than restating it. Look for one before writing" + (shared ? "; in a shared flow, say \"the project's art skill or recipe, if it has one\"." : "."),
                "   - The usual shape: an art director turns the task into prompts (subject, style, size, how many candidates, seeds); an artist generates them " +
                "and hands on the folder they're in and what made each; a checker opens the pictures (agents can read images) against the brief (hands, eyes, " +
                "anatomy, style, anything ruled out) and sends back the ones that fail with what to change.",
                "   - Picking is mine: the last stage puts the candidates in front of me as an image review (a *.review.json in its artifacts folder, which Darg " +
                "shows in its own picker; its briefing says how), and the flow ends there. My picks come back to that agent as a message.",
                "   - Forge not running or a model missing is a reason for `FLOW: STOP` with what to start, not for falling back to something else.",
            ]);
        }
        lines.AddRange([
            "4. Read the file back: a # name line, a line saying what it's for, Max rounds, then a ## section per stage. Edit no other file.",
            "5. Don't commit: the flows folder isn't a repo.",
            "",
            "When you stop, say the flow's name and its stages in a line. Darg reads the flows folder again when your turn ends, so it's in the Flow list then.",
        ]);
        return string.Join(Environment.NewLine, lines);
    }
}
